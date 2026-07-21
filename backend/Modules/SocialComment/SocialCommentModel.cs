using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment.Enums;
using Backend.Shared;

namespace Backend.Modules.SocialComment;

public class SocialCommentModel : BaseEntity
{
    public Guid SocialChannelId { get; set; }
    public Guid SocialPostId { get; set; }
    public SocialPlatform Platform { get; set; }
    public string ExternalCommentId { get; set; } = string.Empty;
    public string? ParentExternalCommentId { get; set; }
    public Guid? ParentCommentId { get; set; }
    public string? AuthorExternalId { get; set; }
    public string? AuthorName { get; set; }
    public string? AuthorUsername { get; set; }
    public string? Message { get; set; }
    public string? PermalinkUrl { get; set; }
    public DateTime? CommentedAt { get; set; }
    public bool IsHidden { get; set; }
    public bool IsFromPage { get; set; }
    public bool IsPending { get; set; }
    public bool IsDeletedOnPlatform { get; set; }
    public int LikeCount { get; set; }
    public int ReplyCount { get; set; }
    public CommentInboxStatus InboxStatus { get; set; } = CommentInboxStatus.New;
    public string? AssignedTo { get; set; }
    public string? InternalNote { get; set; }
    public DateTime? RepliedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
}
