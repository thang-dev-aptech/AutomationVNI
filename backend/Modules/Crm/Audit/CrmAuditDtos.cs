namespace Backend.Modules.Crm.Audit;

public class CrmAuditLogResponse
{
    public Guid Id { get; set; }
    public Guid? CrmCustomerId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string? ActorUserName { get; set; }
    public string? PayloadJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
