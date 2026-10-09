namespace Backend.Modules.Crm.ScheduledMessages;

public class CreateCrmScheduledMessageRequest
{
    public Guid PageConversationId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime ScheduledAtUtc { get; set; }
}

public class UpdateCrmScheduledMessageRequest
{
    public string Text { get; set; } = string.Empty;
    public DateTime ScheduledAtUtc { get; set; }
}

public class CrmScheduledMessageResponse
{
    public Guid Id { get; set; }
    public Guid PageConversationId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime ScheduledAtUtc { get; set; }
    public CrmScheduledMessageStatus Status { get; set; }
    public string? Error { get; set; }
    public string? SentMessageId { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public class CrmScheduledMessageWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 30;
}
