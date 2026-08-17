using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Backend.Shared.TikTok;

public class TikTokOAuthService(
    IOptions<TikTokOAuthOptions> options,
    IMemoryCache cache,
    IHttpClientFactory httpClientFactory,
    TikTokProfileSyncService profileSyncService,
    ILogger<TikTokOAuthService> logger) : ITikTokOAuthService
{
    private const string StateCachePrefix = "tiktok_oauth_state:";
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    private static readonly string[] PlaceholderSecrets =
        ["PASTE_SECRET_HERE", "YOUR_TIKTOK_CLIENT_SECRET", "CHANGE_ME"];

    public string? DescribeConfigIssue()
    {
        var o = options.Value;

        if (string.IsNullOrWhiteSpace(o.ClientKey))
            return "Thiếu TikTokOAuth:ClientKey. Set qua: dotnet user-secrets set \"TikTokOAuth:ClientKey\" \"<client-key>\".";

        var secret = o.ClientSecret?.Trim();
        if (string.IsNullOrWhiteSpace(secret)
            || PlaceholderSecrets.Contains(secret, StringComparer.OrdinalIgnoreCase))
            return "Thiếu hoặc còn placeholder ở TikTokOAuth:ClientSecret. Set qua: dotnet user-secrets set \"TikTokOAuth:ClientSecret\" \"<client-secret>\".";

        if (string.IsNullOrWhiteSpace(o.RedirectUri))
            return "Thiếu TikTokOAuth:RedirectUri. Phải khớp TikTok Developer Portal → Redirect URI.";

        if (NormalizeScopeCsv().Length == 0)
            return "Thiếu TikTokOAuth:Scopes. Tối thiểu cần user.info.basic và video.publish.";

        return null;
    }

    public bool IsConfigured() => DescribeConfigIssue() is null;

    /// <summary>Scope CSV đã trim/dedupe. user.info.basic luôn có mặt — /v2/user/info/ cần nó.</summary>
    private string NormalizeScopeCsv()
    {
        var scopes = options.Value.Scopes
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .ToList();

        if (scopes.Count > 0 && !scopes.Contains("user.info.basic", StringComparer.OrdinalIgnoreCase))
            scopes.Insert(0, "user.info.basic");

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
            new TikTokOAuthStateEntry { UserId = userId, UserName = userName },
            StateTtl);

        var scope = NormalizeScopeCsv();
        var query = string.Join("&",
        [
            $"client_key={Uri.EscapeDataString(o.ClientKey)}",
            $"scope={Uri.EscapeDataString(scope)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(o.RedirectUri)}",
            $"state={Uri.EscapeDataString(state)}"
        ]);

        logger.LogInformation("TikTok connect-url built with scopes [{Scopes}]", scope);
        return $"{o.AuthBaseUrl.TrimEnd('/')}/v2/auth/authorize/?{query}";
    }

    public async Task<TikTokOAuthCallbackResult> HandleCallbackAsync(
        string code, string state, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("OAuth code is missing");

        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("OAuth state is missing");

        if (!cache.TryGetValue<TikTokOAuthStateEntry>(StateCachePrefix + state, out var stateEntry)
            || stateEntry is null)
            throw new InvalidOperationException("OAuth state invalid or expired");

        cache.Remove(StateCachePrefix + state);

        // Khác Facebook/Threads: token TikTok hết hạn ngay ~24h, không có bước "đổi lên long-lived" —
        // response đổi code đã kèm sẵn open_id nên không cần gọi /me riêng để lấy id.
        var token = await ExchangeCodeForTokenAsync(code, ct);
        var profile = await FetchUserProfileAsync(token.AccessToken, ct);

        var scopes = NormalizeScopeCsv();
        logger.LogInformation(
            "TikTok OAuth callback for user {UserId}: profile={OpenId} ({DisplayName}), accessExpiresAt={AccessExp:o}, refreshExpiresAt={RefreshExp:o}",
            stateEntry.UserId, profile.OpenId, profile.DisplayName, token.AccessTokenExpiresAt, token.RefreshTokenExpiresAt);

        var result = await profileSyncService.SyncAsync(
            profile, token, scopes, stateEntry.UserName, ct);

        result.GrantedScopes = scopes;
        result.TokenExpiresAt = token.AccessTokenExpiresAt;
        result.DisplayName = profile.DisplayName;
        return result;
    }

    private async Task<TikTokTokenResult> ExchangeCodeForTokenAsync(string code, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(TikTokOAuthService));

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_key"] = o.ClientKey,
            ["client_secret"] = o.ClientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = o.RedirectUri
        });

        var url = $"{o.ApiBaseUrl.TrimEnd('/')}/{o.ApiVersion}/oauth/token/";
        using var response = await client.PostAsync(url, form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = DescribeTikTokError(body, o);
            logger.LogWarning("TikTok token exchange failed (HTTP {StatusCode}): {Error}", (int)response.StatusCode, error);
            throw new InvalidOperationException(error);
        }

        var parsed = ParseTokenResponse(body);
        if (parsed is null)
        {
            logger.LogWarning("TikTok token response missing access_token: {Body}", body);
            throw new InvalidOperationException("TikTok token response không có access_token.");
        }

        return parsed;
    }

    public async Task<TikTokTokenResult?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(TikTokOAuthService));

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_key"] = o.ClientKey,
            ["client_secret"] = o.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        });

        var url = $"{o.ApiBaseUrl.TrimEnd('/')}/{o.ApiVersion}/oauth/token/";

        try
        {
            using var response = await client.PostAsync(url, form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "TikTok token refresh failed (HTTP {StatusCode}): {Error}",
                    (int)response.StatusCode, DescribeTikTokError(body, o));
                return null;
            }

            return ParseTokenResponse(body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TikTok token refresh error");
            return null;
        }
    }

    /// <summary>Parse response đổi/refresh token thành TikTokTokenResult, hoặc null nếu thiếu access_token.</summary>
    private static TikTokTokenResult? ParseTokenResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
            return null;

        var now = DateTime.UtcNow;
        return new TikTokTokenResult
        {
            AccessToken = accessToken,
            AccessTokenExpiresAt = root.TryGetProperty("expires_in", out var exp)
                && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out var s1) && s1 > 0
                    ? now.AddSeconds(s1) : now.AddHours(24),
            RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? string.Empty : string.Empty,
            RefreshTokenExpiresAt = root.TryGetProperty("refresh_expires_in", out var rexp)
                && rexp.ValueKind == JsonValueKind.Number && rexp.TryGetInt64(out var s2) && s2 > 0
                    ? now.AddSeconds(s2) : now.AddDays(365),
            OpenId = root.TryGetProperty("open_id", out var oid) ? oid.GetString() ?? string.Empty : string.Empty,
            Scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() : null
        };
    }

    private async Task<TikTokUserProfileDto> FetchUserProfileAsync(string accessToken, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(TikTokOAuthService));

        var url = $"{o.ApiBaseUrl.TrimEnd('/')}/{o.ApiVersion}/user/info/" +
                  $"?fields={Uri.EscapeDataString("open_id,display_name,avatar_url")}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = DescribeTikTokError(body, o);
            logger.LogWarning("TikTok /v2/user/info/ failed (HTTP {StatusCode}): {Error}", (int)response.StatusCode, error);
            throw new InvalidOperationException($"Không thể lấy thông tin tài khoản TikTok: {error}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        // TikTok Business API luôn bọc response trong {data:{...}, error:{code,message}} —
        // kể cả khi HTTP 200, phải kiểm tra error.code != "ok" mới coi là thành công thật.
        if (root.TryGetProperty("error", out var errEl)
            && errEl.TryGetProperty("code", out var codeEl)
            && !string.Equals(codeEl.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
        {
            var message = errEl.TryGetProperty("message", out var m) ? m.GetString() : "unknown error";
            throw new InvalidOperationException($"Không thể lấy thông tin tài khoản TikTok: {message}");
        }

        if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("user", out var user))
            throw new InvalidOperationException("TikTok /v2/user/info/ response không có data.user.");

        return new TikTokUserProfileDto
        {
            OpenId = user.TryGetProperty("open_id", out var oid) ? oid.GetString() ?? string.Empty : string.Empty,
            DisplayName = user.TryGetProperty("display_name", out var dn) ? dn.GetString() : null,
            AvatarUrl = user.TryGetProperty("avatar_url", out var av) ? av.GetString() : null
        };
    }

    /// <summary>
    /// TikTok dùng 2 hình dạng lỗi khác nhau: endpoint OAuth (token) trả flat {"error","error_description"}
    /// kiểu chuẩn OAuth2, còn endpoint Business API (v2/user/info, publish) trả nested {"error":{"code","message"}}.
    /// </summary>
    private static string DescribeTikTokError(string body, TikTokOAuthOptions o)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "unknown error";

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.Object)
                {
                    var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                    var code = err.TryGetProperty("code", out var c) ? c.GetString() : null;
                    var text = string.IsNullOrWhiteSpace(message) ? "unknown error" : message!.Trim();
                    return code is null ? text : $"{text} (code {code})";
                }

                if (err.ValueKind == JsonValueKind.String)
                {
                    var description = root.TryGetProperty("error_description", out var d) ? d.GetString() : null;
                    var text = string.IsNullOrWhiteSpace(description) ? err.GetString() : description;
                    return $"{text} ({err.GetString()}). RedirectUri đang gửi: '{o.RedirectUri}' — phải khớp TikTok Developer Portal.";
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON body — tránh leak raw content.
        }

        return "unknown error";
    }
}
