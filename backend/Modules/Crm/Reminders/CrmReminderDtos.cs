namespace Backend.Modules.Crm.Reminders;

public class CreateCrmReminderRequest
{
    public Guid CrmCustomerId { get; set; }
    /// <summary>Tuỳ chọn: gắn nhắc việc vào cơ hội (phải tồn tại, chưa xoá, cùng khách).</summary>
    public Guid? CrmOpportunityId { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Thời điểm đến hạn (UTC). Client có thể gửi ISO UTC.</summary>
    public DateTime DueAtUtc { get; set; }
    public Guid? AssigneeUserId { get; set; }
}

public class UpdateCrmReminderRequest
{
    public string Title { get; set; } = string.Empty;
    public DateTime DueAtUtc { get; set; }
    public Guid? AssigneeUserId { get; set; }
}

public class CrmReminderResponse
{
    public Guid Id { get; set; }
    public Guid CrmCustomerId { get; set; }
    public string? CustomerName { get; set; }
    public Guid? CrmOpportunityId { get; set; }
    public string? OpportunityTitle { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime DueAtUtc { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? NotifiedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CrmReminderBucketsResponse
{
    public List<CrmReminderResponse> Today { get; set; } = [];
    public List<CrmReminderResponse> Overdue { get; set; } = [];
    public List<CrmReminderResponse> Upcoming { get; set; } = [];
}
