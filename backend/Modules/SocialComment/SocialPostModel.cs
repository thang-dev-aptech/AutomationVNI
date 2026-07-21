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
}
