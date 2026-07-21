using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment.Enums;
using Backend.Shared;

namespace Backend.Modules.SocialComment;

public class SocialCommentFilterRequest : PagedFilterRequest
{
    public SocialPlatform? Platform { get; set; }
    public Guid? SocialChannelId { get; set; }
    public Guid? SocialPostId { get; set; }
    public CommentInboxStatus? InboxStatus { get; set; }
    public bool? UnrepliedOnly { get; set; }
    public bool? IsHidden { get; set; }
    public bool? IsPending { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public class SocialCommentCapabilities
{
    public bool CanReply { get; set; }
    public bool CanHide { get; set; }
    public bool CanUnhide { get; set; }
    public bool CanDelete { get; set; }
    public bool CanManagePending { get; set; }
    public bool CanMention { get; set; }
}

public class SocialCommentResponse
{
    public Guid Id { get; set; }
    public Guid SocialChannelId { get; set; }
    public string? ChannelName { get; set; }
    public Guid SocialPostId { get; set; }
    public string? PostMessage { get; set; }
    public string? PostPermalinkUrl { get; set; }
    public Guid? LocalPostId { get; set; }
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
    public CommentInboxStatus InboxStatus { get; set; }
    public string? AssignedTo { get; set; }
    public string? InternalNote { get; set; }
    public DateTime? RepliedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public SocialCommentCapabilities Capabilities { get; set; } = new();
    public List<SocialCommentResponse> Replies { get; set; } = [];
}

public class ReplyCommentRequest
{
    public string Message { get; set; } = string.Empty;
}

public class SetCommentStatusRequest
{
    public CommentInboxStatus Status { get; set; }
}

public class AssignCommentRequest
{
    public string? AssignedTo { get; set; }
}

public class CommentNoteRequest
{
    public string Note { get; set; } = string.Empty;
}

public class SyncCommentsRequest
{
    public Guid? SocialChannelId { get; set; }
    public SocialCommentSyncMode Mode { get; set; } = SocialCommentSyncMode.Recent;
}

public class SyncCommentsResult
{
    public int ChannelsProcessed { get; set; }
    public int PostsUpserted { get; set; }
    public int CommentsUpserted { get; set; }
    public List<string> Errors { get; set; } = [];
}

public class CommentActionLogResponse
{
    public Guid Id { get; set; }
    public CommentActionType ActionType { get; set; }
    public string? ActorUserName { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ExternalResultId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class InboxSummaryResponse
{
    public int Total { get; set; }
    public int NewCount { get; set; }
    public int InProgress { get; set; }
    public int Unreplied { get; set; }
    public int Hidden { get; set; }
    public int Pending { get; set; }
}
