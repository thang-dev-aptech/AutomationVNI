using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Backend.Shared.Threads;

public class ThreadsOAuthService(
    IOptions<ThreadsOAuthOptions> options,
    IMemoryCache cache,
    IHttpClientFactory httpClientFactory,
    ThreadsProfileSyncService profileSyncService,
    ILogger<ThreadsOAuthService> logger) : IThreadsOAuthService
{
    private const string StateCachePrefix = "threads_oauth_state:";
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    // Threads short-lived tokens last 1 hour; long-lived ones 60 days and MUST be refreshed
    // before that or they expire permanently.
    private static readonly TimeSpan LongLivedFallbackLifetime = TimeSpan.FromDays(60);
    private static readonly TimeSpan ShortLivedFallbackLifetime = TimeSpan.FromHours(1);

    /// <summary>Threads rejects a non-tester account while the app is in development mode.</summary>
    private const long TesterInviteNotAcceptedCode = 1349245;

    private static readonly string[] PlaceholderSecrets =
        ["PASTE_SECRET_HERE", "YOUR_THREADS_APP_SECRET", "CHANGE_ME"];

    public string? DescribeConfigIssue()
    {
        var o = options.Value;

        if (string.IsNullOrWhiteSpace(o.AppId))
            return "Thiếu ThreadsOAuth:AppId. Set qua: dotnet user-secrets set \"ThreadsOAuth:AppId\" \"<threads-app-id>\". " +
                   "Lưu ý dùng App ID của Threads (App Dashboard → Threads → Settings), không phải App ID Facebook.";

        var secret = o.AppSecret?.Trim();
        if (string.IsNullOrWhiteSpace(secret)
            || PlaceholderSecrets.Contains(secret, StringComparer.OrdinalIgnoreCase))
            return "Thiếu hoặc còn placeholder ở ThreadsOAuth:AppSecret. Set qua: dotnet user-secrets set \"ThreadsOAuth:AppSecret\" \"<threads-app-secret>\".";

        if (string.IsNullOrWhiteSpace(o.RedirectUri))
            return "Thiếu ThreadsOAuth:RedirectUri. Phải khớp Threads → Settings → Redirect Callback URLs.";

        if (NormalizeScopeCsv().Length == 0)
            return "Thiếu ThreadsOAuth:Scopes. Tối thiểu cần threads_basic.";

        return null;
    }

    public bool IsConfigured() => DescribeConfigIssue() is null;

    /// <summary>Configured scopes as a clean CSV — trims blanks and dedupes. threads_basic is always included.</summary>
    private string NormalizeScopeCsv()
    {
        var scopes = options.Value.Scopes
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        // threads_basic is required by every Threads endpoint — never let config omit it.
        if (scopes.Count > 0 && !scopes.Contains("threads_basic", StringComparer.OrdinalIgnoreCase))
            scopes.Insert(0, "threads_basic");

        return string.Join(",", scopes.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    public string BuildConnectUrl(Guid userId, string userName)
    {
        var issue = DescribeConfigIssue();
        if (issue is not null)
            throw new InvalidOperationException(issue);

        var o = options.Value;
        var state = Guid.NewGuid().ToString("N");
        cache.Set(
            StateCachePrefix + state,
            new ThreadsOAuthStateEntry { UserId = userId, UserName = userName },
            StateTtl);

        var scope = NormalizeScopeCsv();
        var query = string.Join("&",
        [
            $"client_id={Uri.EscapeDataString(o.AppId)}",
            $"redirect_uri={Uri.EscapeDataString(o.RedirectUri)}",
            $"scope={Uri.EscapeDataString(scope)}",
            "response_type=code",
            $"state={Uri.EscapeDataString(state)}"
        ]);

        logger.LogInformation("Threads connect-url built with scopes [{Scopes}]", scope);
        return $"{o.AuthorizeUrl.TrimEnd('/')}?{query}";
    }

    public async Task<ThreadsOAuthCallbackResult> HandleCallbackAsync(
        string code, string state, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("OAuth code is missing");

        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("OAuth state is missing");

        if (!cache.TryGetValue<ThreadsOAuthStateEntry>(StateCachePrefix + state, out var stateEntry)
            || stateEntry is null)
            throw new InvalidOperationException("OAuth state invalid or expired");

        cache.Remove(StateCachePrefix + state);

        // Threads returns the authorization code with a trailing "#_" fragment marker on some
        // redirects; it is not part of the code and the exchange fails if left in.
        var cleanCode = code.Split('#')[0];

        // 1) code -> short-lived (1h) token, 2) upgrade to long-lived (60d) so the refresh worker can keep it alive.
        var shortLived = await ExchangeCodeForTokenAsync(cleanCode, ct);
        var (token, expiresAt) = await ExchangeForLongLivedTokenAsync(shortLived.AccessToken, ct);

        var profile = await FetchUserProfileAsync(token, ct);

        // The token exchange already returns user_id; prefer /me but fall back so a profile-field
        // permission gap never blocks the connect.
        if (string.IsNullOrWhiteSpace(profile.Id))
            profile.Id = shortLived.UserId ?? string.Empty;

        if (string.IsNullOrWhiteSpace(profile.Id))
            throw new InvalidOperationException("Threads không trả về user id — không thể lưu kết nối.");

        var scopes = NormalizeScopeCsv();
        logger.LogInformation(
            "Threads OAuth callback for user {UserId}: profile={ProfileId} ({Username}), expiresAt={ExpiresAt:o}",
            stateEntry.UserId, profile.Id, profile.Username, expiresAt);

        var result = await profileSyncService.SyncAsync(
            profile, token, expiresAt, scopes, stateEntry.UserName, ct);

        result.GrantedScopes = scopes;
        result.TokenExpiresAt = expiresAt;
        result.Username = profile.Username;
        return result;
    }

    private async Task<(string AccessToken, string? UserId)> ExchangeCodeForTokenAsync(
        string code, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(ThreadsOAuthService));

        // Unlike Facebook's GET exchange, Threads requires POST with a form-encoded body.
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = o.AppId,
            ["client_secret"] = o.AppSecret,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = o.RedirectUri,
            ["code"] = code
        });

        var url = $"{o.GraphBaseUrl.TrimEnd('/')}/oauth/access_token";
        using var response = await client.PostAsync(url, form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = DescribeThreadsError(body, o);
            logger.LogWarning(
                "Threads token exchange failed (HTTP {StatusCode}): {Error}",
                (int)response.StatusCode, error);
            throw new InvalidOperationException(error);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var token = root.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Threads token response không có access_token.");

        // user_id arrives as a JSON number, not a string.
        string? userId = null;
        if (root.TryGetProperty("user_id", out var uid))
        {
            userId = uid.ValueKind == JsonValueKind.Number
                ? uid.GetInt64().ToString()
                : uid.GetString();
        }

        return (token, userId);
    }

    /// <summary>
    /// Exchange a 1-hour token for a 60-day one (grant_type=th_exchange_token). Best-effort:
    /// on failure fall back to the short-lived token so Connect still succeeds, with a 1h expiry
    /// recorded so the UI shows the truth.
    /// </summary>
    private async Task<(string Token, DateTime ExpiresAt)> ExchangeForLongLivedTokenAsync(
        string shortLivedToken, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(ThreadsOAuthService));

        var url = $"{o.GraphBaseUrl.TrimEnd('/')}/access_token" +
                  $"?grant_type=th_exchange_token" +
                  $"&client_secret={Uri.EscapeDataString(o.AppSecret)}" +
                  $"&access_token={Uri.EscapeDataString(shortLivedToken)}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Threads long-lived token exchange failed (HTTP {StatusCode}): {Error}. Using short-lived token.",
                    (int)response.StatusCode, DescribeThreadsError(body, o));
                return (shortLivedToken, DateTime.UtcNow.Add(ShortLivedFallbackLifetime));
            }

            var parsed = ParseTokenResponse(body);
            if (parsed is null)
            {
                logger.LogWarning("Threads long-lived token response missing access_token. Using short-lived token.");
                return (shortLivedToken, DateTime.UtcNow.Add(ShortLivedFallbackLifetime));
            }

            return parsed.Value;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Threads long-lived token exchange error; using short-lived token");
            return (shortLivedToken, DateTime.UtcNow.Add(ShortLivedFallbackLifetime));
        }
    }

    public async Task<(string Token, DateTime ExpiresAt)?> RefreshLongLivedTokenAsync(
        string token, CancellationToken ct = default)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(ThreadsOAuthService));

        var url = $"{o.GraphBaseUrl.TrimEnd('/')}/refresh_access_token" +
                  $"?grant_type=th_refresh_token" +
                  $"&access_token={Uri.EscapeDataString(token)}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Threads token refresh failed (HTTP {StatusCode}): {Error}",
                    (int)response.StatusCode, DescribeThreadsError(body, o));
                return null;
            }

            return ParseTokenResponse(body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Threads token refresh error");
            return null;
        }
    }

    /// <summary>Parse {access_token, expires_in} into a token + absolute expiry, or null when malformed.</summary>
    private static (string Token, DateTime ExpiresAt)? ParseTokenResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var token = root.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var expiresAt = root.TryGetProperty("expires_in", out var exp)
            && exp.ValueKind == JsonValueKind.Number
            && exp.TryGetInt64(out var seconds) && seconds > 0
                ? DateTime.UtcNow.AddSeconds(seconds)
                : DateTime.UtcNow.Add(LongLivedFallbackLifetime);

        return (token, expiresAt);
    }

    private async Task<ThreadsUserProfileDto> FetchUserProfileAsync(
        string accessToken, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(ThreadsOAuthService));

        const string fields = "id,username,name,threads_profile_picture_url";
        var url = $"{o.GraphBaseUrl.TrimEnd('/')}/{o.GraphVersion}/me" +
                  $"?fields={Uri.EscapeDataString(fields)}" +
                  $"&access_token={Uri.EscapeDataString(accessToken)}";

        using var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = DescribeThreadsError(body, o);
            logger.LogWarning("Threads /me failed (HTTP {StatusCode}): {Error}", (int)response.StatusCode, error);
            throw new InvalidOperationException($"Không thể lấy thông tin tài khoản Threads: {error}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        return new ThreadsUserProfileDto
        {
            Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Username = root.TryGetProperty("username", out var u) ? u.GetString() : null,
            Name = root.TryGetProperty("name", out var n) ? n.GetString() : null,
            PictureUrl = root.TryGetProperty("threads_profile_picture_url", out var p) ? p.GetString() : null
        };
    }

    /// <summary>
    /// Turn a Threads error body into an actionable Vietnamese message. Threads uses two different
    /// error shapes — nested {"error":{...}} like Graph, and flat {"error_message","error_code"} on
    /// the OAuth endpoints — so both are handled.
    /// </summary>
    private static string DescribeThreadsError(string body, ThreadsOAuthOptions o)
    {
        var (code, message) = ParseThreadsError(body);

        if (code == TesterInviteNotAcceptedCode)
        {
            return "Tài khoản Threads này chưa accept lời mời tester. " +
                   "Vào App Dashboard → App roles → Roles → Threads Testers để mời username Threads, " +
                   "rồi mở app Threads → Settings → Account → Website permissions → Invites → Accept. " +
                   "Kể cả tài khoản admin của app cũng phải được mời và accept riêng.";
        }

        var text = string.IsNullOrWhiteSpace(message) ? "unknown error" : message!.Trim();
        if (text.Length > 300) text = text[..300];

        if (text.Contains("redirect", StringComparison.OrdinalIgnoreCase))
        {
            return $"{text} (code {code?.ToString() ?? "?"}). " +
                   $"Redirect URI đang gửi là '{o.RedirectUri}' — phải khớp tuyệt đối với Threads → Settings → Redirect Callback URLs.";
        }

        return code.HasValue ? $"{text} (code {code})" : text;
    }

    /// <summary>Parse both Threads error shapes into (code, message). Nulls when absent or non-JSON.</summary>
    private static (long? Code, string? Message) ParseThreadsError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Graph-style: {"error":{"message":..,"code":..}}
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                long? code = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
                    && c.TryGetInt64(out var cv) ? cv : null;
                return (code, message);
            }

            // Flat OAuth-style: {"error_message":..,"error_code":..}
            if (root.TryGetProperty("error_message", out var em))
            {
                long? code = root.TryGetProperty("error_code", out var ec) && ec.ValueKind == JsonValueKind.Number
                    && ec.TryGetInt64(out var ecv) ? ecv : null;
                return (code, em.GetString());
            }
        }
        catch (JsonException)
        {
            // Non-JSON body — avoid leaking raw content.
        }

        return (null, null);
    }
}
