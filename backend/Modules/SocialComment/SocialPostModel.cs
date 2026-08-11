using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;

namespace Backend.Modules.SocialComment;

/// <summary>
/// Mirror bài viết từ Facebook/Threads (kể cả bài không tạo trong hệ thống).
/// LocalPostId gắn với Post nội bộ khi ExternalPostId khớp.
/// </summary>
public class SocialPostModel : BaseEntity
{
    public Guid SocialChannelId { get; set; }
    public SocialPlatform Platform { get; set; }
    public string ExternalPostId { get; set; } = string.Empty;
    public Guid? LocalPostId { get; set; }
    public string? Message { get; set; }
    public string? PermalinkUrl { get; set; }
    public DateTime? PostedAt { get; set; }
    public int CommentCount { get; set; }
    public DateTime? LastCommentAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? SyncCursor { get; set; }

    // ═══ Chỉ số tương tác lấy từ nền tảng ═══
    //
    // CommentCount ở trên KHÁC ba cột này. Nó đếm số dòng trong bảng SocialComments của mình —
    // tức chỉ những bình luận đã đồng bộ về, đã trừ bình luận do chính page viết và bình luận
    // bị xoá trên nền tảng. Dùng cho hộp thư.
    //
    // PlatformCommentCount là con số Facebook TỰ báo. Hai số này lệch nhau là bình thường, và
    // đưa cho khách phải là số của Facebook — vì đó là số họ thấy khi mở page ra đối chiếu.
    public int LikeCount { get; set; }
    public int ShareCount { get; set; }
    public int PlatformCommentCount { get; set; }
    public DateTime? MetricsSyncedAt { get; set; }
}
