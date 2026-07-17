using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Backend.Shared.Meta;

public class MetaOAuthService(
    IOptions<MetaOAuthOptions> options,
    IMemoryCache cache,
    IHttpClientFactory httpClientFactory,
    MetaPageSyncService pageSyncService,
    ILogger<MetaOAuthService> logger) : IMetaOAuthService
{
    private const string StateCachePrefix = "meta_oauth_state:";
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    // Long-lived user tokens last ~60 days; page tokens derived from them do not expire.
    private static readonly TimeSpan LongLivedFallbackLifetime = TimeSpan.FromDays(60);
    private static readonly TimeSpan ShortLivedFallbackLifetime = TimeSpan.FromHours(1);

    private static readonly string[] PlaceholderSecrets =
        ["SECRET_MOI_SAU_KHI_RESET", "PASTE_SECRET_HERE", "YOUR_META_APP_SECRET", "CHANGE_ME"];

    /// <summary>
    /// Returns a specific, actionable reason when Meta OAuth is misconfigured, or null when ready.
    /// Powers a precise error at /api/meta/connect-url instead of a generic "not configured".
    /// </summary>
    public string? DescribeConfigIssue()
    {
        var o = options.Value;

        if (string.IsNullOrWhiteSpace(o.AppId))
            return "Thiếu MetaOAuth:AppId. Set qua: dotnet user-secrets set \"MetaOAuth:AppId\" \"<app-id>\".";

        var secret = o.AppSecret?.Trim();
        if (string.IsNullOrWhiteSpace(secret)
            || PlaceholderSecrets.Contains(secret, StringComparer.OrdinalIgnoreCase))
            return "Thiếu hoặc còn placeholder ở MetaOAuth:AppSecret. Set secret thật qua: dotnet user-secrets set \"MetaOAuth:AppSecret\" \"<app-secret>\".";

        if (string.IsNullOrWhiteSpace(o.RedirectUri))
            return "Thiếu MetaOAuth:RedirectUri. Phải khớp Meta App > Valid OAuth Redirect URIs, ví dụ: http://localhost:5068/api/meta/callback.";

        return null;
    }

    public bool IsConfigured() => DescribeConfigIssue() is null;

    /// <summary>Configured scopes as a clean CSV — trims blanks and dedupes to avoid malformed scope strings.</summary>
    private string NormalizeScopeCsv()
        => string.Join(",", options.Value.Scopes
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));

    public string BuildConnectUrl(Guid userId, string userName)
    {
        var issue = DescribeConfigIssue();
        if (issue is not null)
            throw new InvalidOperationException(issue);

        var o = options.Value;
        var state = Guid.NewGuid().ToString("N");
        cache.Set(
            StateCachePrefix + state,
            new MetaOAuthStateEntry { UserId = userId, UserName = userName },
            StateTtl);

        var scope = NormalizeScopeCsv();
        logger.LogInformation("Meta connect-url built with scopes: [{Scopes}]", scope);
        var query = string.Join("&", new[]
        {
            $"client_id={Uri.EscapeDataString(o.AppId)}",
            $"redirect_uri={Uri.EscapeDataString(o.RedirectUri)}",
            $"state={Uri.EscapeDataString(state)}",
            $"scope={Uri.EscapeDataString(scope)}",
            "response_type=code"
        });

        return $"https://www.facebook.com/dialog/oauth?{query}";
    }

    public async Task<MetaOAuthCallbackResult> HandleCallbackAsync(
        string code, string state, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("OAuth code is missing");

        if (string.IsNullOrWhiteSpace(state))
            throw new ArgumentException("OAuth state is missing");

        if (!cache.TryGetValue<MetaOAuthStateEntry>(StateCachePrefix + state, out var stateEntry)
            || stateEntry is null)
            throw new InvalidOperationException("OAuth state invalid or expired");

        cache.Remove(StateCachePrefix + state);

        // 1) code -> short-lived user token, 2) upgrade to long-lived so derived page tokens don't expire.
        var shortLivedToken = await ExchangeCodeForTokenAsync(code, ct);
        var (userToken, userTokenExpiresAt) = await ExchangeForLongLivedTokenAsync(shortLivedToken, ct);

        var profile = await FetchUserProfileAsync(userToken, ct);
        var pages = await FetchManagedPagesAsync(userToken, ct);
        var groups = await FetchManagedGroupsAsync(userToken, ct);

        logger.LogInformation(
            "Meta OAuth callback for user {UserId}: profile={ProfileId}, pages={PageCount}, groups={GroupCount}, userTokenExpiresAt={ExpiresAt:o}",
            stateEntry.UserId, profile.Id, pages.Count, groups.Count, userTokenExpiresAt);

        var scopes = NormalizeScopeCsv();
        return await pageSyncService.SyncAsync(
            profile,
            pages,
            groups,
            scopes,
            userTokenExpiresAt,
            stateEntry.UserName,
            ct);
    }

    private async Task<string> ExchangeCodeForTokenAsync(string code, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(MetaOAuthService));

        var url = $"{o.GraphBaseUrl.TrimEnd('/')}/{o.GraphVersion}/oauth/access_token" +
                  $"?client_id={Uri.EscapeDataString(o.AppId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(o.RedirectUri)}" +
                  $"&client_secret={Uri.EscapeDataString(o.AppSecret)}" +
                  $"&code={Uri.EscapeDataString(code)}";

        using var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var metaError = ExtractMetaError(body);
            logger.LogWarning(
                "Meta token exchange failed (HTTP {StatusCode}): {Error}",
                (int)response.StatusCode, metaError);
            throw new InvalidOperationException(
                $"Không thể đổi OAuth code lấy access token từ Meta: {metaError}. " +
                "Kiểm tra App Secret (user-secrets) và Redirect URI khớp Meta Dashboard: " +
                $"{o.RedirectUri}");
        }

        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Meta token response không có access_token.");

        return token;
    }

    /// <summary>
    /// Exchange a short-lived user token for a long-lived one (~60 days). Page tokens fetched
    /// afterwards inherit long-lived status and effectively never expire. Best-effort: on failure,
    /// fall back to the short-lived token so Connect still works (logged for visibility).
    /// </summary>
    private async Task<(string Token, DateTime? ExpiresAt)> ExchangeForLongLivedTokenAsync(
        string shortLivedToken, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(MetaOAuthService));

        var url = $"{o.GraphBaseUrl.TrimEnd('/')}/{o.GraphVersion}/oauth/access_token" +
                  $"?grant_type=fb_exchange_token" +
                  $"&client_id={Uri.EscapeDataString(o.AppId)}" +
                  $"&client_secret={Uri.EscapeDataString(o.AppSecret)}" +
                  $"&fb_exchange_token={Uri.EscapeDataString(shortLivedToken)}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Meta long-lived token exchange failed (HTTP {StatusCode}): {Error}. Using short-lived token.",
                    (int)response.StatusCode, ExtractMetaError(body));
                return (shortLivedToken, DateTime.UtcNow.Add(ShortLivedFallbackLifetime));
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var token = root.TryGetProperty("access_token", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(token))
            {
                logger.LogWarning("Meta long-lived token response missing access_token. Using short-lived token.");
                return (shortLivedToken, DateTime.UtcNow.Add(ShortLivedFallbackLifetime));
            }

            var expiresAt = root.TryGetProperty("expires_in", out var exp)
                && exp.ValueKind == JsonValueKind.Number
                && exp.TryGetInt64(out var seconds) && seconds > 0
                    ? DateTime.UtcNow.AddSeconds(seconds)
                    : DateTime.UtcNow.Add(LongLivedFallbackLifetime);

            return (token, expiresAt);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Meta long-lived token exchange error; using short-lived token");
            return (shortLivedToken, DateTime.UtcNow.Add(ShortLivedFallbackLifetime));
        }
    }

    private async Task<MetaUserProfileDto> FetchUserProfileAsync(
        string userAccessToken, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(MetaOAuthService));

        var url = $"{o.GraphBaseUrl.TrimEnd('/')}/{o.GraphVersion}/me" +
                  $"?fields={Uri.EscapeDataString("id,name,picture.type(large)")}" +
                  $"&access_token={Uri.EscapeDataString(userAccessToken)}";

        using var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var metaError = ExtractMetaError(body);
            logger.LogWarning("Meta /me failed (HTTP {StatusCode}): {Error}", (int)response.StatusCode, metaError);
            throw new InvalidOperationException($"Không thể lấy thông tin tài khoản Meta: {metaError}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        string? pictureUrl = null;
        if (root.TryGetProperty("picture", out var pic)
            && pic.TryGetProperty("data", out var picData)
            && picData.TryGetProperty("url", out var urlEl))
        {
            pictureUrl = urlEl.GetString();
        }

        return new MetaUserProfileDto
        {
            Id = root.GetProperty("id").GetString() ?? string.Empty,
            Name = root.TryGetProperty("name", out var nameEl)
                ? nameEl.GetString() ?? string.Empty
                : string.Empty,
            PictureUrl = pictureUrl
        };
    }

    private async Task<List<MetaPageAccountDto>> FetchManagedPagesAsync(
        string userAccessToken, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(MetaOAuthService));

        const string fields =
            "id,name,access_token,instagram_business_account{id,username,name}";

        var initialUrl = $"{o.GraphBaseUrl.TrimEnd('/')}/{o.GraphVersion}/me/accounts" +
                         $"?fields={Uri.EscapeDataString(fields)}" +
                         $"&limit=100" +
                         $"&access_token={Uri.EscapeDataString(userAccessToken)}";

        // Follow paging.next so accounts with >25 (default page size) pages sync fully.
        var items = await FetchAllDataItemsAsync(initialUrl, client, "me/accounts", throwOnError: true, ct);

        var pages = new List<MetaPageAccountDto>();
        foreach (var item in items)
        {
            if (!item.TryGetProperty("id", out var idEl))
                continue;

            var page = new MetaPageAccountDto
            {
                Id = idEl.GetString() ?? string.Empty,
                Name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty,
                AccessToken = item.TryGetProperty("access_token", out var at) ? at.GetString() : null
            };

            if (item.TryGetProperty("instagram_business_account", out var igEl)
                && igEl.ValueKind == JsonValueKind.Object)
            {
                page.InstagramBusinessAccount = new MetaInstagramBusinessDto
                {
                    Id = igEl.TryGetProperty("id", out var igId) ? igId.GetString() ?? string.Empty : string.Empty,
                    Username = igEl.TryGetProperty("username", out var u) ? u.GetString() : null,
                    Name = igEl.TryGetProperty("name", out var n) ? n.GetString() : null
                };
            }

            pages.Add(page);
        }

        return pages;
    }

    /// <summary>
    /// Best-effort groups sync. Permission/API may fail in Development or after Meta deprecations —
    /// never fail the whole OAuth callback. (Note: Group publishing via API was removed by Meta in 2024.)
    /// </summary>
    private async Task<List<MetaGroupDto>> FetchManagedGroupsAsync(
        string userAccessToken, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(MetaOAuthService));

        const string fields = "id,name,privacy,administrator";
        var initialUrl = $"{o.GraphBaseUrl.TrimEnd('/')}/{o.GraphVersion}/me/groups" +
                         $"?admin_only=true" +
                         $"&fields={Uri.EscapeDataString(fields)}" +
                         $"&limit=100" +
                         $"&access_token={Uri.EscapeDataString(userAccessToken)}";

        try
        {
            var items = await FetchAllDataItemsAsync(initialUrl, client, "me/groups", throwOnError: false, ct);

            var groups = new List<MetaGroupDto>();
            foreach (var item in items)
            {
                if (!item.TryGetProperty("id", out var idEl))
                    continue;

                bool? isAdmin = null;
                if (item.TryGetProperty("administrator", out var adminEl))
                {
                    if (adminEl.ValueKind == JsonValueKind.True) isAdmin = true;
                    else if (adminEl.ValueKind == JsonValueKind.False) isAdmin = false;
                }

                groups.Add(new MetaGroupDto
                {
                    Id = idEl.GetString() ?? string.Empty,
                    Name = item.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                    Privacy = item.TryGetProperty("privacy", out var p) ? p.GetString() : null,
                    Administrator = isAdmin,
                    AccessToken = userAccessToken
                });
            }

            return groups;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Meta /me/groups failed; continuing without groups");
            return [];
        }
    }

    /// <summary>
    /// GET a Graph edge and follow paging.next until exhausted, returning cloned data items.
    /// throwOnError=false → best-effort: stop and return what was collected on first failure.
    /// maxRequests caps pagination to guard against runaway loops.
    /// </summary>
    private async Task<List<JsonElement>> FetchAllDataItemsAsync(
        string initialUrl, HttpClient client, string edge, bool throwOnError,
        CancellationToken ct, int maxRequests = 40)
    {
        var results = new List<JsonElement>();
        var url = initialUrl;
        var requests = 0;

        while (!string.IsNullOrEmpty(url) && requests < maxRequests)
        {
            requests++;
            using var response = await client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var metaError = ExtractMetaError(body);
                if (throwOnError)
                {
                    logger.LogWarning("Meta /{Edge} failed (HTTP {StatusCode}): {Error}", edge, (int)response.StatusCode, metaError);
                    throw new InvalidOperationException($"Không thể lấy dữ liệu {edge} từ Meta: {metaError}");
                }

                logger.LogWarning("Meta /{Edge} skipped (HTTP {StatusCode}): {Error}", edge, (int)response.StatusCode, metaError);
                break;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                    results.Add(item.Clone()); // Clone: element must outlive the disposed JsonDocument.
            }

            url = root.TryGetProperty("paging", out var paging)
                && paging.TryGetProperty("next", out var next)
                    ? next.GetString()
                    : null;
        }

        if (requests >= maxRequests && !string.IsNullOrEmpty(url))
            logger.LogWarning("Meta /{Edge} pagination hit safety cap ({Max} requests); some items may be omitted", edge, maxRequests);

        return results;
    }

    /// <summary>Extract a readable, sanitized message from a Graph API error body.</summary>
    private static string ExtractMetaError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "unknown error";

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                long? code = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
                    && c.TryGetInt64(out var cv) ? cv : null;

                var text = string.IsNullOrWhiteSpace(message) ? "unknown error" : message!.Trim();
                if (text.Length > 300) text = text[..300];
                return code.HasValue ? $"{text} (code {code})" : text;
            }
        }
        catch (JsonException)
        {
            // Non-JSON body — avoid leaking raw content.
        }

        return "unknown error";
    }
}
