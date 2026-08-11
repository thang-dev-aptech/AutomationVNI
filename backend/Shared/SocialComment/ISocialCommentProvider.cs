using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;

namespace Backend.Shared.SocialComment;

public class ProviderPostDto
{
    public string ExternalPostId { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? PermalinkUrl { get; set; }
    public DateTime? PostedAt { get; set; }
    public string? NextCursor { get; set; }

    // Chỉ số tương tác. Nền tảng nào không trả thì để null — KHÁC HẲN với 0.
    // Threads chưa có đường lấy mấy số này; ghi 0 vào là dashboard hiện "0 like" cho một page
    // thật ra đang chạy tốt, mà không ai biết đó là "chưa đo được".
    public int? LikeCount { get; set; }
    public int? ShareCount { get; set; }
    public int? CommentCount { get; set; }
}

/// <summary>Thông tin hồ sơ của page — dùng cho thẻ "Người theo dõi" trên dashboard.</summary>
public class ProviderPageProfileDto
{
    public string? Name { get; set; }
    public int? Followers { get; set; }
}

public class ProviderCommentDto
{
    public string ExternalCommentId { get; set; } = string.Empty;
    public string? ParentExternalCommentId { get; set; }
    public string? AuthorExternalId { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorUsername { get; set; }
    public string? Message { get; set; }
    public string? PermalinkUrl { get; set; }
    public DateTime? CommentedAt { get; set; }
    public bool IsHidden { get; set; }
    public bool IsFromPage { get; set; }
    public bool IsPending { get; set; }
    public int LikeCount { get; set; }
    public int ReplyCount { get; set; }
}

public class ProviderActionResult
{
    public bool Success { get; set; }
    public string? ExternalId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public static ProviderActionResult Ok(string? externalId = null) => new()
    {
        Success = true,
        ExternalId = externalId
    };

    public static ProviderActionResult Fail(string code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message
    };
}

public interface ISocialCommentProvider
{
    SocialPlatform Platform { get; }
    SocialCommentCapabilities Capabilities { get; }

    Task<(List<ProviderPostDto> Items, string? NextCursor)> ListPostsAsync(
        string externalPageId,
        string accessToken,
        string? cursor,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Hồ sơ page (tên, số người theo dõi). Nền tảng chưa hỗ trợ thì trả null — người gọi phải
    /// hiểu null là "chưa đo được", không phải "bằng 0".
    /// </summary>
    Task<ProviderPageProfileDto?> GetPageProfileAsync(
        string externalPageId,
        string accessToken,
        CancellationToken ct = default);

    Task<(List<ProviderCommentDto> Items, string? NextCursor)> ListCommentsAsync(
        string externalPostId,
        string accessToken,
        string? cursor,
        int limit,
        CancellationToken ct = default);

    Task<ProviderCommentDto?> GetCommentAsync(
        string externalCommentId,
        string accessToken,
        CancellationToken ct = default);

    Task<ProviderActionResult> ReplyAsync(
        string externalCommentId,
        string accessToken,
        string message,
        string? pageExternalId = null,
        CancellationToken ct = default);

    Task<ProviderActionResult> HideAsync(
        string externalCommentId,
        string accessToken,
        bool hide,
        CancellationToken ct = default);

    Task<ProviderActionResult> DeleteAsync(
        string externalCommentId,
        string accessToken,
        CancellationToken ct = default);

    Task<ProviderActionResult> ManagePendingAsync(
        string externalCommentId,
        string accessToken,
        bool approve,
        CancellationToken ct = default);
}
