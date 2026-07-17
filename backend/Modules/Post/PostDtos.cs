using Backend.Modules.GenerationJob.Enums;
using Backend.Modules.Post.Enums;
using Backend.Shared;

namespace Backend.Modules.Post;

public class CreatePostRequest
{
    public string Title { get; set; } = string.Empty;
    public Guid SocialChannelId { get; set; }
    public Guid? CategoryId { get; set; }
    public GenerationFlow GenerationFlow { get; set; } = GenerationFlow.FullAI;
    /// <summary>Mục tiêu bài viết (idea/goal) — lưu ExtraJson, đưa vào prompt AI khi sinh text.</summary>
    public string? Objective { get; set; }
}

public class UpdatePostRequest
{
    public string? Title { get; set; }
    public string? Content { get; set; }
    public Guid? CategoryId { get; set; }
    // Status / ScheduledPublishAt không cho update trực tiếp — dùng workflow endpoints
}

public class PostFilterRequest : PagedFilterRequest
{
    public PostStatus? Status { get; set; }
    public GenerationFlow? GenerationFlow { get; set; }
    public Guid? SocialChannelId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class PostResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid SocialChannelId { get; set; }
    public GenerationFlow GenerationFlow { get; set; }
    public PostStatus Status { get; set; }
    public Guid UserId { get; set; }
    public DateTime? ScheduledPublishAt { get; set; }
    public string? ScheduleTimezone { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? ExternalPostId { get; set; }
    public string? PublishedUrl { get; set; }
    public string? RejectionReason { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

// --- Workflow DTOs ---

public class RejectPostRequest
{
    public string Reason { get; set; } = string.Empty;
}

public class SchedulePostRequest
{
    public DateTime ScheduledAt { get; set; }
    public string? Timezone { get; set; }
}

public class PostGenerationStatusResponse
{
    public Guid PostId { get; set; }
    public PostStatus PostStatus { get; set; }
    public string? GenerationError { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public List<GenerationStepResponse> Steps { get; set; } = [];
}

public class GenerationStepResponse
{
    public Guid JobId { get; set; }
    public JobType JobType { get; set; }
    public JobStatus JobStatus { get; set; }
    public JobFlowType FlowType { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputPayload { get; set; }
    public Guid? MediaAssetId { get; set; }
    public Guid? PostMediaId { get; set; }
    public string? PublicUrl { get; set; }
}

public class PostTimelineResponse
{
    public Guid PostId { get; set; }
    public PostStatus CurrentStatus { get; set; }
    public List<TimelineEntryResponse> Events { get; set; } = [];
}

public class TimelineEntryResponse
{
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Detail { get; set; }
}
