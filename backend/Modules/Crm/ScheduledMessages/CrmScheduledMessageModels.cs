using Backend.Shared;

namespace Backend.Modules.Crm.ScheduledMessages;

public enum CrmScheduledMessageStatus
{
    Pending = 1,
    Sending = 2,
    Sent = 3,
    Failed = 4,
    Cancelled = 5
}

/// <summary>Tin nhắn Messenger hẹn giờ gửi. Gửi tối đa một lần: Pending → Sending (claim nguyên tử) → Sent/Failed.</summary>
public class CrmScheduledMessageModel : BaseEntity
{
    public Guid PageConversationId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime ScheduledAtUtc { get; set; }
    public CrmScheduledMessageStatus Status { get; set; } = CrmScheduledMessageStatus.Pending;
    public string? Error { get; set; }
    /// <summary>ExternalMessageId (Graph) của tin echo đã gửi.</summary>
    public string? SentMessageId { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? ClaimedAtUtc { get; set; }
    public Guid? ClaimToken { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
