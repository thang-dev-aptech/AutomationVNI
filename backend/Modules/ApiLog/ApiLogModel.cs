using Backend.Shared;

namespace Backend.Modules.ApiLog;

public class ApiLogModel : BaseEntity
{
    public string Endpoint { get; set; } = string.Empty;
    public string Controller { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }
    public int ResponseStatus { get; set; }
    public long TimelineMs { get; set; }
    public Guid? CallByUserId { get; set; }
    public string? CallByUserName { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? ErrorMessage { get; set; }
}
