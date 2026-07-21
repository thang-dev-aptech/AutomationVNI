using Backend.Modules.SocialComment.Enums;
using Backend.Shared;

namespace Backend.Modules.SocialComment;

public class CommentActionLogModel : BaseEntity
{
    public Guid SocialCommentId { get; set; }
    public CommentActionType ActionType { get; set; }
    public string? ActorUserName { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? PayloadJson { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? ExternalResultId { get; set; }
}
