using Backend.Modules.GenerationJob.Enums;
using Backend.Shared;

namespace Backend.Modules.GenerationJob;

public class GenerationJobModel : BaseEntity
{
    public Guid PostId { get; set; }
    public JobType JobType { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public JobFlowType FlowType { get; set; } = JobFlowType.FullAI;
    public int Priority { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public string? IdempotencyKey { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? InputPayload { get; set; }
    public string? OutputPayload { get; set; }
}
