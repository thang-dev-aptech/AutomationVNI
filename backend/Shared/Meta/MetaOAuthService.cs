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

    public bool IsConfigured()
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.AppId)
            || string.IsNullOrWhiteSpace(o.AppSecret)
            || string.IsNullOrWhiteSpace(o.RedirectUri))
            return false;

        // Guard against leftover placeholder from setup docs.
        var secret = o.AppSecret.Trim();
        if (secret is "SECRET_MOI_SAU_KHI_RESET"
            or "PASTE_SECRET_HERE"
            or "YOUR_META_APP_SECRET"
            or "CHANGE_ME")
            return false;

        return true;
    }

    public string BuildConnectUrl(Guid userId, string userName)
    {
        if (!IsConfigured())
            throw new InvalidOperationException(
                "Meta OAuth chưa cấu hình. Set MetaOAuth:AppId và MetaOAuth:AppSecret qua user-secrets.");

        var o = options.Value;
        var state = Guid.NewGuid().ToString("N");
        cache.Set(
            StateCachePrefix + state,
            new MetaOAuthStateEntry { UserId = userId, UserName = userName },
            StateTtl);

        var scope = string.Join(",", o.Scopes.Distinct(StringComparer.OrdinalIgnoreCase));
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

        var userToken = await ExchangeCodeForTokenAsync(code, ct);
        var profile = await FetchUserProfileAsync(userToken, ct);
        var pages = await FetchManagedPagesAsync(userToken, ct);
        var groups = await FetchManagedGroupsAsync(userToken, ct);

        logger.LogInformation(
            "Meta OAuth callback for user {UserId}: profile={ProfileId}, pages={PageCount}, groups={GroupCount}",
            stateEntry.UserId, profile.Id, pages.Count, groups.Count);

        var scopes = string.Join(",", options.Value.Scopes.Distinct(StringComparer.OrdinalIgnoreCase));
        return await pageSyncService.SyncAsync(
            profile,
            pages,
            groups,
            scopes,
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
            logger.LogWarning(
                "Meta token exchange failed with HTTP {StatusCode}. Body (sanitized length={Len})",
                (int)response.StatusCode, body.Length);
            throw new InvalidOperationException(
                "Không thể đổi OAuth code lấy access token từ Meta. Kiểm tra App Secret (user-secrets) và Redirect URI khớp Meta Dashboard.");
        }

        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Meta token response không có access_token.");

        return token;
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
            logger.LogWarning("Meta /me failed with HTTP {StatusCode}", (int)response.StatusCode);
            throw new InvalidOperationException("Không thể lấy thông tin tài khoản Meta.");
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

        var url = $"{o.GraphBaseUrl.TrimEnd('/')}/{o.GraphVersion}/me/accounts" +
                  $"?fields={Uri.EscapeDataString(fields)}" +
                  $"&access_token={Uri.EscapeDataString(userAccessToken)}";

        using var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Meta /me/accounts failed with HTTP {StatusCode}", (int)response.StatusCode);
            throw new InvalidOperationException("Không thể lấy danh sách Facebook Pages từ Meta.");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data))
            return [];

        var pages = new List<MetaPageAccountDto>();
        foreach (var item in data.EnumerateArray())
        {
            var page = new MetaPageAccountDto
            {
                Id = item.GetProperty("id").GetString() ?? string.Empty,
                Name = item.GetProperty("name").GetString() ?? string.Empty,
                AccessToken = item.TryGetProperty("access_token", out var at)
                    ? at.GetString()
                    : null
            };

            if (item.TryGetProperty("instagram_business_account", out var igEl)
                && igEl.ValueKind == JsonValueKind.Object)
            {
                page.InstagramBusinessAccount = new MetaInstagramBusinessDto
                {
                    Id = igEl.GetProperty("id").GetString() ?? string.Empty,
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
    /// never fail the whole OAuth callback.
    /// </summary>
    private async Task<List<MetaGroupDto>> FetchManagedGroupsAsync(
        string userAccessToken, CancellationToken ct)
    {
        var o = options.Value;
        var client = httpClientFactory.CreateClient(nameof(MetaOAuthService));

        const string fields = "id,name,privacy,administrator";
        var url = $"{o.GraphBaseUrl.TrimEnd('/')}/{o.GraphVersion}/me/groups" +
                  $"?admin_only=true" +
                  $"&fields={Uri.EscapeDataString(fields)}" +
                  $"&access_token={Uri.EscapeDataString(userAccessToken)}";

        try
        {
            using var response = await client.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Meta /me/groups skipped (HTTP {StatusCode}). Enable groups permission in Meta App if needed.",
                    (int)response.StatusCode);
                return [];
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data))
                return [];

            var groups = new List<MetaGroupDto>();
            foreach (var item in data.EnumerateArray())
            {
                bool? isAdmin = null;
                if (item.TryGetProperty("administrator", out var adminEl))
                {
                    if (adminEl.ValueKind == JsonValueKind.True) isAdmin = true;
                    else if (adminEl.ValueKind == JsonValueKind.False) isAdmin = false;
                }

                groups.Add(new MetaGroupDto
                {
                    Id = item.GetProperty("id").GetString() ?? string.Empty,
                    Name = item.TryGetProperty("name", out var n)
                        ? n.GetString() ?? string.Empty
                        : string.Empty,
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
}
