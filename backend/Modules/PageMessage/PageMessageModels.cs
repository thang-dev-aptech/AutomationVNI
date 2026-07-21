using Backend.Shared;

namespace Backend.Modules.PageMessage;

public enum MessageInboxStatus
{
    New = 1,
    InProgress = 2,
    Replied = 3,
    Ignored = 4
}

public enum MessageActionType
{
    Reply = 1,
    Assign = 2,
    SetStatus = 3,
    AddNote = 4,
    Sync = 5
}

/// <summary>Mirror một hội thoại Messenger giữa Facebook Page và một người dùng.</summary>
public class PageConversationModel : BaseEntity
{
    public Guid SocialChannelId { get; set; }
    public string ExternalConversationId { get; set; } = string.Empty;
    public string ParticipantExternalId { get; set; } = string.Empty;
    public string? ParticipantName { get; set; }
    public string? ParticipantAvatarUrl { get; set; }
    public string? Snippet { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime? LastCustomerMessageAt { get; set; }
    public DateTime? LastPageMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public int MessageCount { get; set; }
    public MessageInboxStatus InboxStatus { get; set; } = MessageInboxStatus.New;
    public string? AssignedTo { get; set; }
    public string? InternalNote { get; set; }
    public DateTime? LastSyncedAt { get; set; }
}

public class PageMessageModel : BaseEntity
{
    public Guid PageConversationId { get; set; }
    public Guid SocialChannelId { get; set; }
    public string ExternalMessageId { get; set; } = string.Empty;
    public string? SenderExternalId { get; set; }
    public string? RecipientExternalId { get; set; }
    public string? Text { get; set; }
    public string? AttachmentsJson { get; set; }
    public bool IsFromPage { get; set; }
    public bool IsEcho { get; set; }
    public bool IsDelivered { get; set; }
    public bool IsRead { get; set; }
    public DateTime? SentAt { get; set; }
}

public class MessageActionLogModel : BaseEntity
{
    public Guid PageConversationId { get; set; }
    public MessageActionType ActionType { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorUserName { get; set; }
    public string? PayloadJson { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? ExternalResultId { get; set; }
}
