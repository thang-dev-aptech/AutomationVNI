using System.Net.Http.Headers;
using System.Text.Json;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;
using Backend.Shared.SocialPublish;
using Microsoft.Extensions.Options;

namespace Backend.Shared.SocialComment;

public class FacebookCommentProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<SocialPublishOptions> options,
    ILogger<FacebookCommentProvider> logger) : ISocialCommentProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SocialPlatform Platform => SocialPlatform.Facebook;

    public SocialCommentCapabilities Capabilities { get; } = new()
    {
        CanReply = true,
        CanHide = true,
        CanUnhide = true,
        CanDelete = true,
        CanManagePending = false,
        CanMention = true
    };

    public async Task<(List<ProviderPostDto> Items, string? NextCursor)> ListPostsAsync(
        string externalPageId, string accessToken, string? cursor, int limit, CancellationToken ct = default)
    {
        var fb = options.Value.Facebook;

        // summary(true).limit(0) = "cho tôi CON SỐ TỔNG, đừng gửi danh sách".
        //
        // Thiếu .limit(0) thì Facebook kèm theo 25 người like đầu tiên của MỖI bài — với 50 bài
        // là hơn một nghìn object thừa mỗi lượt gọi, chậm và tốn băng thông, trong khi thứ cần
        // dùng chỉ là một số nguyên.
        //
        // Ba trường này KHÔNG cần thêm quyền: đo thật trên page thật bằng đúng token đang có
        // (pages_manage_posts + pages_manage_metadata + pages_show_list) đều trả về số.
        // Ngược lại, mọi chỉ số qua /insights — tiếp cận, lượt hiển thị — trả về mảng rỗng vì
        // thiếu quyền read_insights, thứ phải qua App Review của Facebook mới có.
        var fields = "id,message,permalink_url,created_time,"
                   + "likes.summary(true).limit(0),comments.summary(true).limit(0),shares";
        var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(externalPageId)}/published_posts" +
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
                    Message = GetString(item, "message"),
                    PermalinkUrl = GetString(item, "permalink_url"),
                    PostedAt = GetDateTime(item, "created_time"),
                    LikeCount = GetSummaryTotal(item, "likes"),
                    CommentCount = GetSummaryTotal(item, "comments"),
                    ShareCount = GetSharesCount(item),
                });
            }
        }

        var next = GetPagingCursor(doc?.RootElement, "after");
        return (items, next);
    }

    public async Task<(List<ProviderCommentDto> Items, string? NextCursor)> ListCommentsAsync(
        string externalPostId, string accessToken, string? cursor, int limit, CancellationToken ct = default)
    {
        var fb = options.Value.Facebook;
        var fields = "id,message,created_time,from{id,name},permalink_url,is_hidden,comment_count,like_count,parent{id}";
        var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(externalPostId)}/comments" +
                  $"?fields={Uri.EscapeDataString(fields)}" +
                  $"&filter=stream" +
                  $"&order=chronological" +
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
        var fb = options.Value.Facebook;
        var fields = "id,message,created_time,from{id,name},permalink_url,is_hidden,comment_count,like_count,parent{id}";
        var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(externalCommentId)}" +
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
            return ProviderActionResult.Fail("FB_EMPTY_REPLY", "Nội dung trả lời trống");

        var fb = options.Value.Facebook;
        var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(externalCommentId)}/comments";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["message"] = message.Trim(),
            ["access_token"] = accessToken
        });
        return await PostActionAsync(url, content, "id", ct);
    }

    public async Task<ProviderActionResult> HideAsync(
        string externalCommentId, string accessToken, bool hide, CancellationToken ct = default)
    {
        var fb = options.Value.Facebook;
        var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(externalCommentId)}";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["is_hidden"] = hide ? "true" : "false",
            ["access_token"] = accessToken
        });
        return await PostActionAsync(url, content, null, ct);
    }

    public async Task<ProviderActionResult> DeleteAsync(
        string externalCommentId, string accessToken, CancellationToken ct = default)
    {
        var fb = options.Value.Facebook;
        var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(externalCommentId)}" +
                  $"?access_token={Uri.EscapeDataString(accessToken)}";
        var client = httpClientFactory.CreateClient(nameof(FacebookCommentProvider));
        using var response = await client.DeleteAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Facebook delete comment failed: {Status} {Body}", response.StatusCode, Truncate(body));
            return ProviderActionResult.Fail("FB_DELETE_FAILED", Truncate(body));
        }
        return ProviderActionResult.Ok(externalCommentId);
    }

    public Task<ProviderActionResult> ManagePendingAsync(
        string externalCommentId, string accessToken, bool approve, CancellationToken ct = default)
        => Task.FromResult(ProviderActionResult.Fail(
            "FB_PENDING_UNSUPPORTED",
            "Facebook Page comments không dùng pending approval như Threads."));

    private ProviderCommentDto? MapComment(JsonElement item)
    {
        var id = GetString(item, "id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        string? authorId = null, authorName = null;
        if (item.TryGetProperty("from", out var from) && from.ValueKind == JsonValueKind.Object)
        {
            authorId = GetString(from, "id");
            authorName = GetString(from, "name");
        }

        string? parentId = null;
        if (item.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object)
            parentId = GetString(parent, "id");

        return new ProviderCommentDto
        {
            ExternalCommentId = id,
            ParentExternalCommentId = parentId,
            AuthorExternalId = authorId,
            AuthorName = authorName,
            Message = GetString(item, "message"),
            PermalinkUrl = GetString(item, "permalink_url"),
            CommentedAt = GetDateTime(item, "created_time"),
            IsHidden = item.TryGetProperty("is_hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True,
            LikeCount = item.TryGetProperty("like_count", out var likes) && likes.TryGetInt32(out var lc) ? lc : 0,
            ReplyCount = item.TryGetProperty("comment_count", out var replies) && replies.TryGetInt32(out var rc) ? rc : 0
        };
    }

    private async Task<ProviderActionResult> PostActionAsync(
        string url, HttpContent content, string? idField, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(nameof(FacebookCommentProvider));
        using var response = await client.PostAsync(url, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Facebook comment action failed: {Status} {Body}", response.StatusCode, Truncate(body));
            return ProviderActionResult.Fail("FB_ACTION_FAILED", Truncate(body));
        }

        string? externalId = null;
        if (!string.IsNullOrWhiteSpace(idField))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                externalId = GetString(doc.RootElement, idField);
            }
            catch { /* ignore */ }
        }

        return ProviderActionResult.Ok(externalId);
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(nameof(FacebookCommentProvider));
        using var response = await client.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Facebook Graph GET failed: {Status} {Body}", response.StatusCode, Truncate(body));
            return null;
        }

        try { return JsonDocument.Parse(body); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Facebook Graph response parse failed");
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

    /// <summary>
    /// Đọc <c>{ "likes": { "summary": { "total_count": 12 } } }</c>.
    ///
    /// Trả null khi trường không có, KHÔNG trả 0. Bài mà token không đọc được tương tác sẽ thiếu
    /// hẳn trường này; ghi 0 vào là dashboard tự tin báo "bài này không ai like" trong khi thật ra
    /// mình chưa đo được. Sai kiểu đó không có cách nào phát hiện từ giao diện.
    /// </summary>
    private static int? GetSummaryTotal(JsonElement el, string edge)
    {
        if (!el.TryGetProperty(edge, out var node)) return null;
        if (!node.TryGetProperty("summary", out var summary)) return null;
        return summary.TryGetProperty("total_count", out var total)
               && total.ValueKind == JsonValueKind.Number
            ? total.GetInt32()
            : null;
    }

    /// <summary>
    /// Chia sẻ có hình dạng khác hẳn like và bình luận: <c>{ "shares": { "count": 3 } }</c>,
    /// không có tầng "summary".
    ///
    /// Và Facebook BỎ HẲN trường shares khi bài chưa ai chia sẻ, chứ không gửi count = 0. Nên ở
    /// đây null nghĩa là "không có ai chia sẻ" — khác với like/bình luận, nơi null nghĩa là
    /// "chưa đo được". Người gọi quy về 0 là đúng với riêng trường này.
    /// </summary>
    private static int? GetSharesCount(JsonElement el)
    {
        if (!el.TryGetProperty("shares", out var node)) return 0;
        return node.TryGetProperty("count", out var count) && count.ValueKind == JsonValueKind.Number
            ? count.GetInt32()
            : 0;
    }

    public async Task<ProviderPageProfileDto?> GetPageProfileAsync(
        string externalPageId, string accessToken, CancellationToken ct = default)
    {
        var fb = options.Value.Facebook;
        // followers_count là số hiển thị trên page. fan_count là số "người thích" kiểu cũ —
        // Facebook đã gộp hai khái niệm và trên phần lớn page hai số bằng nhau, nhưng con số
        // người dùng nhìn thấy là followers_count. Lấy cả hai để còn đường lui.
        var url = $"{fb.GraphBaseUrl.TrimEnd('/')}/{fb.GraphVersion}/{Uri.EscapeDataString(externalPageId)}" +
                  $"?fields={Uri.EscapeDataString("name,followers_count,fan_count")}" +
                  $"&access_token={Uri.EscapeDataString(accessToken)}";

        using var doc = await GetJsonAsync(url, ct);
        if (doc is null) return null;

        var root = doc.RootElement;
        var followers = GetInt(root, "followers_count") ?? GetInt(root, "fan_count");
        return new ProviderPageProfileDto { Name = GetString(root, "name"), Followers = followers };
    }

    private static int? GetInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

    private static string Truncate(string? value, int max = 400)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
