using Backend.Shared;

namespace Backend.Modules.PageMessage;

public class PageConversationFilterRequest : PagedFilterRequest
{
    public Guid? SocialChannelId { get; set; }
    public MessageInboxStatus? InboxStatus { get; set; }
    public bool? UnreadOnly { get; set; }
    public bool? OpenWindowOnly { get; set; }
}

public class PageMessageResponse
{
    public Guid Id { get; set; }
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

public class PageConversationResponse
{
    public Guid Id { get; set; }
    public Guid SocialChannelId { get; set; }
    public string? ChannelName { get; set; }
    public string ExternalConversationId { get; set; } = string.Empty;
    public string ParticipantExternalId { get; set; } = string.Empty;
    public string? ParticipantName { get; set; }
    public string? ParticipantAvatarUrl { get; set; }
    public string? Snippet { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime? LastCustomerMessageAt { get; set; }
    public DateTime? LastPageMessageAt { get; set; }
    public DateTime? ReplyWindowClosesAt { get; set; }
    public bool IsReplyWindowOpen { get; set; }
    public int UnreadCount { get; set; }
    public int MessageCount { get; set; }
    public MessageInboxStatus InboxStatus { get; set; }
    public string? AssignedTo { get; set; }
    public string? InternalNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<PageMessageResponse> Messages { get; set; } = [];
}

public class MessageInboxSummaryResponse
{
    public int Total { get; set; }
    public int NewCount { get; set; }
    public int InProgress { get; set; }
    public int Unread { get; set; }
    public int ReplyWindowOpen { get; set; }
}

public class SendPageMessageRequest
{
    public string Text { get; set; } = string.Empty;
}

public class SetMessageStatusRequest
{
    public MessageInboxStatus Status { get; set; }
}

public class AssignMessageRequest
{
    public string? AssignedTo { get; set; }
}

public class MessageNoteRequest
{
    public string Note { get; set; } = string.Empty;
}

public class SyncPageMessagesRequest
{
    public Guid? SocialChannelId { get; set; }
    public bool Full { get; set; }
}

public class SyncPageMessagesResult
{
    public int ChannelsProcessed { get; set; }
    public int ConversationsUpserted { get; set; }
    public int MessagesUpserted { get; set; }
    public List<string> Errors { get; set; } = [];
}

public class SendPageMessageResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? RecipientId { get; set; }
    public string? ErrorMessage { get; set; }
}
