using Backend.Shared;

namespace Backend.Modules.ApiLog;

public class ApiLogFilterRequest : PagedFilterRequest
{
    public string? Endpoint { get; set; }
    public string? HttpMethod { get; set; }
    public int? ResponseStatus { get; set; }
    public Guid? CallByUserId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class ApiLogResponse
{
    public Guid Id { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string Controller { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public int ResponseStatus { get; set; }
    public long TimelineMs { get; set; }
    public Guid? CallByUserId { get; set; }
    public string? CallByUserName { get; set; }
    public string? IpAddress { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    // RequestPayload / ResponsePayload không trả list — chỉ trả ở GetById
}

public class ApiLogDetailResponse : ApiLogResponse
{
    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }
}
