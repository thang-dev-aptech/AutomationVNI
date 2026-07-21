using System.Text.Json;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;
using Backend.Shared.SocialPublish;
using Microsoft.Extensions.Options;

namespace Backend.Shared.SocialComment;

public class ThreadsCommentProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SocialPublishOptions> options,
    ILogger<ThreadsCommentProvider> logger) : ISocialCommentProvider
{
    public SocialPlatform Platform => SocialPlatform.Threads;

    public SocialCommentCapabilities Capabilities { get; } = new()
    {
        CanReply = true,
        CanHide = true,
        CanUnhide = true,
        CanDelete = false,
        CanManagePending = true,
        CanMention = false
    };

    public async Task<(List<ProviderPostDto> Items, string? NextCursor)> ListPostsAsync(
        string externalPageId, string accessToken, string? cursor, int limit, CancellationToken ct = default)
    {
        var th = options.Value.Threads;
        var fields = "id,text,permalink,timestamp";
        var url = $"{th.GraphBaseUrl.TrimEnd('/')}/{th.GraphVersion}/{Uri.EscapeDataString(externalPageId)}/threads" +
                  $"?fields={Uri.EscapeDataString(fields)}" +
                  $"&limit={Math.Clamp(limit, 1, 100)}" +
                  $"&access_token={Uri.EscapeDataString(accessToken)}";
        if (!string.IsNullOrWhiteSpace(cursor))
            url += $"&after={Uri.EscapeDataString(cursor)}";

        using var doc = await GetJsonAsync(url, ct);
        var items = new List<ProviderPostDto>();
        if (doc?.RootElement.TryGetProperty("data", out var data) == true && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var id = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                items.Add(new ProviderPostDto
                {
                    ExternalPostId = id,
                    Message = GetString(item, "text"),
                    PermalinkUrl = GetString(item, "permalink"),
                    PostedAt = GetDateTime(item, "timestamp")
                });
            }
        }

        return (items, GetPagingCursor(doc?.RootElement, "after"));
    }

    public async Task<(List<ProviderCommentDto> Items, string? NextCursor)> ListCommentsAsync(
        string externalPostId, string accessToken, string? cursor, int limit, CancellationToken ct = default)
    {
        // conversation = flattened top-level + nested replies
        var th = options.Value.Threads;
        var fields = "id,text,username,timestamp,permalink,has_replies,is_reply,replied_to,hide_status,is_reply_owned_by_me";
        var url = $"{th.GraphBaseUrl.TrimEnd('/')}/{th.GraphVersion}/{Uri.EscapeDataString(externalPostId)}/conversation" +
                  $"?fields={Uri.EscapeDataString(fields)}" +
                  $"&reverse=false" +
                  $"&limit={Math.Clamp(limit, 1, 100)}" +
                  $"&access_token={Uri.EscapeDataString(accessToken)}";
        if (!string.IsNullOrWhiteSpace(cursor))
            url += $"&after={Uri.EscapeDataString(cursor)}";

        using var doc = await GetJsonAsync(url, ct);
        var items = new List<ProviderCommentDto>();
        if (doc?.RootElement.TryGetProperty("data", out var data) == true && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var mapped = MapComment(item);
                if (mapped is not null) items.Add(mapped);
            }
        }

        return (items, GetPagingCursor(doc?.RootElement, "after"));
    }

    public async Task<ProviderCommentDto?> GetCommentAsync(
        string externalCommentId, string accessToken, CancellationToken ct = default)
    {
        var th = options.Value.Threads;
        var fields = "id,text,username,timestamp,permalink,has_replies,is_reply,replied_to,hide_status,is_reply_owned_by_me";
        var url = $"{th.GraphBaseUrl.TrimEnd('/')}/{th.GraphVersion}/{Uri.EscapeDataString(externalCommentId)}" +
                  $"?fields={Uri.EscapeDataString(fields)}" +
                  $"&access_token={Uri.EscapeDataString(accessToken)}";
        using var doc = await GetJsonAsync(url, ct);
        return doc is null ? null : MapComment(doc.RootElement);
    }

    public async Task<ProviderActionResult> ReplyAsync(
        string externalCommentId, string accessToken, string message,
        string? pageExternalId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            return ProviderActionResult.Fail("TH_EMPTY_REPLY", "Nội dung trả lời trống");
        if (string.IsNullOrWhiteSpace(pageExternalId))
            return ProviderActionResult.Fail("TH_USER_MISSING", "Thiếu Threads user id để publish reply");

        var th = options.Value.Threads;
        var createUrl = $"{th.GraphBaseUrl.TrimEnd('/')}/{th.GraphVersion}/{Uri.EscapeDataString(pageExternalId)}/threads";
        using var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["media_type"] = "TEXT",
            ["text"] = message.Trim(),
            ["reply_to_id"] = externalCommentId,
            ["access_token"] = accessToken
        });

        var create = await PostJsonAsync(createUrl, createContent, ct);
        if (create is null)
            return ProviderActionResult.Fail("TH_CONTAINER_FAILED", "Không tạo được reply container");

        var creationId = GetString(create.RootElement, "id");
        create.Dispose();
        if (string.IsNullOrWhiteSpace(creationId))
            return ProviderActionResult.Fail("TH_CONTAINER_ID_MISSING", "Threads không trả creation_id");

        // Cho media processing một chút (text thường nhanh)
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        var publishUrl = $"{th.GraphBaseUrl.TrimEnd('/')}/{th.GraphVersion}/{Uri.EscapeDataString(pageExternalId)}/threads_publish";
        using var publishContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["creation_id"] = creationId,
            ["access_token"] = accessToken
        });
        using var published = await PostJsonAsync(publishUrl, publishContent, ct);
        if (published is null)
            return ProviderActionResult.Fail("TH_PUBLISH_FAILED", "Publish reply thất bại");

        var mediaId = GetString(published.RootElement, "id");
        return ProviderActionResult.Ok(mediaId);
    }

    public async Task<ProviderActionResult> HideAsync(
        string externalCommentId, string accessToken, bool hide, CancellationToken ct = default)
    {
        var th = options.Value.Threads;
        var url = $"{th.GraphBaseUrl.TrimEnd('/')}/{th.GraphVersion}/{Uri.EscapeDataString(externalCommentId)}/manage_reply";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["hide"] = hide ? "true" : "false",
            ["access_token"] = accessToken
        });
        using var doc = await PostJsonAsync(url, content, ct);
        return doc is null
            ? ProviderActionResult.Fail("TH_HIDE_FAILED", "Ẩn/hiện reply thất bại")
            : ProviderActionResult.Ok(externalCommentId);
    }

    public Task<ProviderActionResult> DeleteAsync(
        string externalCommentId, string accessToken, CancellationToken ct = default)
        => Task.FromResult(ProviderActionResult.Fail(
            "TH_DELETE_UNSUPPORTED",
            "Threads API không hỗ trợ xóa reply của người khác — chỉ ẩn/hiện."));

    public async Task<ProviderActionResult> ManagePendingAsync(
        string externalCommentId, string accessToken, bool approve, CancellationToken ct = default)
    {
        var th = options.Value.Threads;
        var url = $"{th.GraphBaseUrl.TrimEnd('/')}/{th.GraphVersion}/{Uri.EscapeDataString(externalCommentId)}/manage_pending_reply";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["approve"] = approve ? "true" : "false",
            ["access_token"] = accessToken
        });
        using var doc = await PostJsonAsync(url, content, ct);
        return doc is null
            ? ProviderActionResult.Fail("TH_PENDING_FAILED", "Duyệt pending reply thất bại")
            : ProviderActionResult.Ok(externalCommentId);
    }

    private ProviderCommentDto? MapComment(JsonElement item)
    {
        var id = GetString(item, "id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        string? parentId = null;
        if (item.TryGetProperty("replied_to", out var repliedTo))
        {
            if (repliedTo.ValueKind == JsonValueKind.Object)
                parentId = GetString(repliedTo, "id");
            else if (repliedTo.ValueKind == JsonValueKind.String)
                parentId = repliedTo.GetString();
        }

        var hideStatus = GetString(item, "hide_status");
        var isHidden = string.Equals(hideStatus, "HIDDEN", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(hideStatus, "true", StringComparison.OrdinalIgnoreCase);

        return new ProviderCommentDto
        {
            ExternalCommentId = id,
            ParentExternalCommentId = parentId,
            AuthorUsername = GetString(item, "username"),
            AuthorName = GetString(item, "username"),
            Message = GetString(item, "text"),
            PermalinkUrl = GetString(item, "permalink"),
            CommentedAt = GetDateTime(item, "timestamp"),
            IsHidden = isHidden,
            IsFromPage = item.TryGetProperty("is_reply_owned_by_me", out var owned)
                         && owned.ValueKind == JsonValueKind.True,
            ReplyCount = item.TryGetProperty("has_replies", out var has)
                         && has.ValueKind == JsonValueKind.True
                ? 1
                : 0
        };
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(nameof(ThreadsCommentProvider));
        using var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Threads Graph GET failed: {Status} {Body}", response.StatusCode, Truncate(body));
            return null;
        }

        try { return JsonDocument.Parse(body); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Threads Graph response parse failed");
            return null;
        }
    }

    private async Task<JsonDocument?> PostJsonAsync(string url, HttpContent content, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(nameof(ThreadsCommentProvider));
        using var response = await client.PostAsync(url, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Threads Graph POST failed: {Status} {Body}", response.StatusCode, Truncate(body));
            return null;
        }

        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Threads Graph POST parse failed");
            return null;
        }
    }

    private static string? GetPagingCursor(JsonElement? root, string cursorName)
    {
        if (root is null) return null;
        if (!root.Value.TryGetProperty("paging", out var paging)) return null;
        if (!paging.TryGetProperty("cursors", out var cursors)) return null;
        return GetString(cursors, cursorName);
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static DateTime? GetDateTime(JsonElement el, string name)
    {
        var raw = GetString(el, name);
        return DateTime.TryParse(raw, out var dt) ? dt.ToUniversalTime() : null;
    }

    private static string Truncate(string? value, int max = 400)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
