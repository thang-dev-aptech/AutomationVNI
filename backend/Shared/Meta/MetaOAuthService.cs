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

    private const string CredsCheckCacheKey = "meta_oauth_creds_ok";

    /// <summary>
    /// Live check that Meta recognizes the configured App ID + Secret via a client_credentials app token.
    /// Catches the #1 Connect failure — an invalid/deleted App ID (Graph code 101 "Invalid Client ID",
    /// or code 190 "cannot get application info") that otherwise only surfaces as Facebook's opaque
    /// "Nội dung này hiện không hiển thị" dialog. Positive result cached 5 min; transient/network errors
    /// return null so a Graph hiccup never blocks a healthy setup.
    /// </summary>
    public async Task<string?> DescribeLiveConfigIssueAsync(CancellationToken ct = default)
    {
        var staticIssue = DescribeConfigIssue();
        if (staticIssue is not null)
            return staticIssue;

        if (cache.TryGetValue<bool>(CredsCheckCacheKey, out var ok) && ok)
            return null;

        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(MetaOAuthService));
        var url = $"{o.GraphBaseUrl.TrimEnd('/')}/{o.GraphVersion}/oauth/access_token" +
                  $"?client_id={Uri.EscapeDataString(o.AppId)}" +
                  $"&client_secret={Uri.EscapeDataString(o.AppSecret)}" +
                  $"&grant_type=client_credentials";

        try
        {
            using var response = await client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("access_token", out var t)
                    && !string.IsNullOrWhiteSpace(t.GetString()))
                {
                    cache.Set(CredsCheckCacheKey, true, TimeSpan.FromMinutes(5));
                    return null;
                }
            }

            var (code, message) = ParseMetaError(body);
            logger.LogWarning(
                "Meta credential preflight failed for AppId {AppId} (code {Code}): {Message}",
                o.AppId, code, message);

            // 101 Invalid Client ID / 190 cannot-get-application-info → the App ID is not a valid, live app.
            var msgHasInvalidId = message is not null
                && (message.Contains("Invalid Client ID", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("application info", StringComparison.OrdinalIgnoreCase));
            if (code is 101 or 190 || msgHasInvalidId)
            {
                return $"Meta không nhận diện được App ID '{o.AppId}' (Invalid Client ID, code {code?.ToString() ?? "?"}). " +
                       "App có thể đã bị xóa/khóa, hoặc bạn đang dùng nhầm Business Portfolio ID / Page ID thay vì App ID. " +
                       "Mở developers.facebook.com/apps → chọn app → Settings → Basic, copy đúng App ID rồi chạy: " +
                       "dotnet user-secrets set \"MetaOAuth:AppId\" \"<app-id>\" và restart backend.";
            }

            // Graph code 1 / message mentioning secret → App ID exists but the Secret is wrong.
            if (message is not null && message.Contains("secret", StringComparison.OrdinalIgnoreCase))
            {
                return "Sai App Secret cho App ID này. Lấy lại tại Meta App → Settings → Basic → App Secret rồi chạy: " +
                       "dotnet user-secrets set \"MetaOAuth:AppSecret\" \"<app-secret>\" và restart backend.";
            }

            return $"Meta từ chối App ID/Secret khi kiểm tra (code {code?.ToString() ?? "?"}): {message ?? "unknown error"}. " +
                   "Kiểm tra lại MetaOAuth:AppId và MetaOAuth:AppSecret.";
        }
        catch (Exception ex)
        {
            // Network/transient error — best-effort, do not block Connect on a Graph hiccup.
            logger.LogWarning(ex, "Meta credential preflight skipped (transient error)");
            return null;
        }
    }

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
        var oAuthParams = new List<string>
        {
            $"client_id={Uri.EscapeDataString(o.AppId)}",
            $"redirect_uri={Uri.EscapeDataString(o.RedirectUri)}",
            $"state={Uri.EscapeDataString(state)}",
            "response_type=code",
            // Buộc Facebook hiển thị lại màn duyệt quyền — để khi thêm scope mới (vd pages_manage_posts)
            // người dùng cấp được quyền mới, thay vì bị bỏ qua vì đã grant lần trước.
            "auth_type=rerequest"
        };

        // Facebook Login for Business requires config_id. Without it Meta often shows
        // "Nội dung này hiện không hiển thị" / "This content isn't available" after login.
        if (!string.IsNullOrWhiteSpace(o.ConfigId))
        {
            oAuthParams.Add($"config_id={Uri.EscapeDataString(o.ConfigId.Trim())}");
            logger.LogInformation(
                "Meta connect-url built with config_id (Login for Business). scopes_optional=[{Scopes}]",
                scope);
        }
        else if (!string.IsNullOrWhiteSpace(scope))
        {
            oAuthParams.Add($"scope={Uri.EscapeDataString(scope)}");
            logger.LogInformation(
                "Meta connect-url built with classic scopes (no ConfigId): [{Scopes}]",
                scope);
        }
        else
        {
            throw new InvalidOperationException(
                "Meta OAuth thiếu quyền. Set MetaOAuth:ConfigId (Facebook Login for Business) " +
                "hoặc MetaOAuth:Scopes (Facebook Login cổ điển).");
        }

        var query = string.Join("&", oAuthParams);
        // Keep dialog URL unversioned — /vXX.X/dialog/oauth can trigger INVALID_APP_ID on some apps.
        var dialogUrl = $"https://www.facebook.com/dialog/oauth?{query}";

        // If a non-role FB session is already logged in, Meta shows a blank "content unavailable"
        // page instead of an Allow dialog. Force re-login so the user can pick the Admin/Tester account.
        if (o.ForceReLogin)
        {
            logger.LogInformation("Meta connect-url wrapping dialog with logout redirect (ForceReLogin=true)");
            return "https://www.facebook.com/logout.php?next=" + Uri.EscapeDataString(dialogUrl);
        }

        return dialogUrl;
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
        var grantedPerms = await FetchGrantedPermissionsAsync(userToken, ct);
        var pages = await FetchManagedPagesAsync(userToken, ct);
        var groups = await FetchManagedGroupsAsync(userToken, ct);

        var missingToken = pages.Count(p => string.IsNullOrWhiteSpace(p.AccessToken));
        logger.LogInformation(
            "Meta OAuth callback for user {UserId}: profile={ProfileId}, pages={PageCount} (missingToken={MissingToken}), groups={GroupCount}, granted=[{Granted}], userTokenExpiresAt={ExpiresAt:o}",
            stateEntry.UserId, profile.Id, pages.Count, missingToken, groups.Count,
            string.Join(",", grantedPerms), userTokenExpiresAt);

        if (pages.Count == 0)
        {
            var hasPagesShowList = grantedPerms.Any(p =>
                p.Equals("pages_show_list", StringComparison.OrdinalIgnoreCase)
                || p.Equals("pages_manage_metadata", StringComparison.OrdinalIgnoreCase));
            logger.LogWarning(
                "Meta /me/accounts returned 0 pages. hasPagesListPerm={HasPerm}, granted=[{Granted}]. " +
                "Classic Login: user must select Pages in the Facebook permission dialog; " +
                "Development mode requires the FB account to be Admin/Developer/Tester on the app.",
                hasPagesShowList, string.Join(",", grantedPerms));
        }

        var scopes = NormalizeScopeCsv();
        var result = await pageSyncService.SyncAsync(
            profile,
            pages,
            groups,
            scopes,
            userTokenExpiresAt,
            stateEntry.UserName,
            ct);
        result.GrantedPermissions = string.Join(",", grantedPerms);
        return result;
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

    private async Task<List<string>> FetchGrantedPermissionsAsync(
        string userAccessToken, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(MetaOAuthService));
        var url = $"{o.GraphBaseUrl.TrimEnd('/')}/{o.GraphVersion}/me/permissions" +
                  $"?access_token={Uri.EscapeDataString(userAccessToken)}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Meta /me/permissions failed: {Error}", ExtractMetaError(body));
                return [];
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return [];

            var granted = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                var status = item.TryGetProperty("status", out var st) ? st.GetString() : null;
                var permission = item.TryGetProperty("permission", out var p) ? p.GetString() : null;
                if (string.Equals(status, "granted", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(permission))
                    granted.Add(permission!);
            }

            return granted;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Meta /me/permissions error");
            return [];
        }
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

        // Granular page permissions sometimes list a Page without embedding access_token —
        // try a direct Page lookup with the user token before giving up.
        foreach (var page in pages.Where(p =>
                     !string.IsNullOrWhiteSpace(p.Id) && string.IsNullOrWhiteSpace(p.AccessToken)))
        {
            page.AccessToken = await TryFetchPageAccessTokenAsync(page.Id, userAccessToken, client, ct);
            if (string.IsNullOrWhiteSpace(page.AccessToken))
            {
                logger.LogWarning(
                    "Meta page {PageId} ({PageName}) listed in /me/accounts but no access_token — cannot publish until user re-grants Page access",
                    page.Id, page.Name);
            }
        }

        return pages;
    }

    private async Task<string?> TryFetchPageAccessTokenAsync(
        string pageId, string userAccessToken, HttpClient client, CancellationToken ct)
    {
        var o = options.Value;
        var url = $"{o.GraphBaseUrl.TrimEnd('/')}/{o.GraphVersion}/{Uri.EscapeDataString(pageId)}" +
                  $"?fields=access_token" +
                  $"&access_token={Uri.EscapeDataString(userAccessToken)}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Meta page {PageId} token lookup failed: {Error}",
                    pageId, ExtractMetaError(body));
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("access_token", out var at)
                ? at.GetString()
                : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Meta page {PageId} token lookup error", pageId);
            return null;
        }
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

    /// <summary>Parse a Graph API error body into (code, message) for classification. Nulls when absent.</summary>
    private static (long? Code, string? Message) ParseMetaError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                long? code = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
                    && c.TryGetInt64(out var cv) ? cv : null;
                return (code, message);
            }
        }
        catch (JsonException)
        {
            // Non-JSON body — nothing to classify.
        }

        return (null, null);
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
