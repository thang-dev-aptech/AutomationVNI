using Backend.Modules.GenerationJob.Enums;
using Backend.Shared;

namespace Backend.Modules.GenerationJob;

public class CreateGenerationJobRequest
{
    public Guid PostId { get; set; }
    public JobType JobType { get; set; }
    public JobFlowType FlowType { get; set; } = JobFlowType.FullAI;
    public int Priority { get; set; }
    public int MaxRetries { get; set; } = 3;
    public DateTime? ScheduledAt { get; set; }
    public string? InputPayload { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class UpdateGenerationJobRequest
{
    public JobStatus? Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputPayload { get; set; }
    public int? RetryCount { get; set; }
}

public class GenerationJobFilterRequest : PagedFilterRequest
{
    public Guid? PostId { get; set; }
    public JobType? JobType { get; set; }
    public JobStatus? Status { get; set; }
    public JobFlowType? FlowType { get; set; }
}

public class FailGenerationJobRequest
{
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public class GenerationJobResponse
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public JobType JobType { get; set; }
    public JobStatus Status { get; set; }
    public JobFlowType FlowType { get; set; }
    public int Priority { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputPayload { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class QueueGenerationResponse
{
    public Guid PostId { get; set; }
    public Guid JobId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public JobStatus JobStatus { get; set; }
    public JobType JobType { get; set; }
}

public class QueueTextGenerationResponse : QueueGenerationResponse;

public class QueueImageGenerationResponse : QueueGenerationResponse;

public class QueueImageRenderResponse : QueueGenerationResponse;

public class QueueMediaMatchResponse : QueueGenerationResponse;

public class ProcessGenerationJobResponse
{
    public Guid JobId { get; set; }
    public Guid PostId { get; set; }
    public JobType JobType { get; set; }
    public JobStatus JobStatus { get; set; }
    public string? OutputPayload { get; set; }
    public Guid? MediaAssetId { get; set; }
    public Guid? PostMediaId { get; set; }
    public string? PublicUrl { get; set; }
}

public class MockTextGenerationResult
{
    public string Content { get; set; } = string.Empty;
    public List<string> Hashtags { get; set; } = [];
    public string Cta { get; set; } = string.Empty;
    public string ImagePrompt { get; set; } = string.Empty;
}

public class TextGenerationJobOutput
{
    public string Source { get; set; } = "mock";
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string Content { get; set; } = string.Empty;
    public List<string> Hashtags { get; set; } = [];
    public string Cta { get; set; } = string.Empty;
    public string ImagePrompt { get; set; } = string.Empty;
    public string? BannerHeadline { get; set; }
    public string? BannerSubheadline { get; set; }
    public string? BannerCta { get; set; }
}

public class MockImageGenerationResult
{
    public string Prompt { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string? PublicUrl { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public MediaAsset.Enums.MediaSource Source { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? AltText { get; set; }
    public string? Description { get; set; }
}
