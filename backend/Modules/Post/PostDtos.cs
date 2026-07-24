using Backend.Modules.GenerationJob.Enums;
using Backend.Modules.Post.Enums;
using Backend.Shared;

namespace Backend.Modules.Post;

public class CreatePostRequest
{
    public string Title { get; set; } = string.Empty;
    /// <summary>1 kênh (tương thích cũ). Ưu tiên SocialChannelIds nếu có.</summary>
    public Guid SocialChannelId { get; set; }
    /// <summary>Nhiều kênh — fan-out cùng 1 ý tưởng (mỗi kênh 1 bài).</summary>
    public List<Guid>? SocialChannelIds { get; set; }
    /// <summary>
    /// Template danh mục (text + ảnh). Bắt buộc nếu có kênh chưa cấu hình PageContext
    /// (default template / prompt inline). Có PageContext sẵn thì có thể bỏ trống.
    /// </summary>
    public Guid? PromptTemplateId { get; set; }
    public Guid? CategoryId { get; set; }
    public GenerationFlow GenerationFlow { get; set; } = GenerationFlow.FullAI;
    /// <summary>Legacy — giữ cho bulk cũ. Tạo bài đơn dùng PromptTemplateId.</summary>
    public string? Objective { get; set; }
    public Guid? TextTemplateId { get; set; }
    public Guid? ImageTemplateId { get; set; }
}

// --- Bulk (tạo hàng loạt) ---

public class BulkPostItem
{
    /// <summary>Ý tưởng → Title + prompt.</summary>
    public string Idea { get; set; } = string.Empty;
    public string? Objective { get; set; }
    public Guid? CategoryId { get; set; }
    /// <summary>Override danh mục theo dòng (hiếm dùng). Null = dùng PromptTemplateId của batch.</summary>
    public Guid? PromptTemplateId { get; set; }
    public Guid? TextTemplateId { get; set; }
    public Guid? ImageTemplateId { get; set; }
}

public class BulkCreatePostRequest
{
    public List<BulkPostItem> Items { get; set; } = [];
    /// <summary>Fan-out: mỗi item được tạo cho MỖI channel trong danh sách.</summary>
    public List<Guid> ChannelIds { get; set; } = [];
    public GenerationFlow GenerationFlow { get; set; } = GenerationFlow.FullAI;
    /// <summary>Danh mục template (text+ảnh) chung cho cả batch — bắt buộc.</summary>
    public Guid? PromptTemplateId { get; set; }
    /// <summary>Legacy — không dùng trên UI mới.</summary>
    public Guid? CategoryId { get; set; }
    public Guid? TextTemplateId { get; set; }
    public Guid? ImageTemplateId { get; set; }
}

public class BulkCreateResult
{
    public Guid BatchId { get; set; }
    public int Created { get; set; }
    public List<Guid> PostIds { get; set; } = [];
}

public class BulkTargetRequest
{
    public Guid? BatchId { get; set; }
    public List<Guid>? PostIds { get; set; }
}

public class BulkScheduleRequest : BulkTargetRequest
{
    /// <summary>Mốc bắt đầu (UTC). Bỏ trống = từ bây giờ.</summary>
    public DateTime? StartAtUtc { get; set; }
    /// <summary>Khung giờ vàng trong ngày (local), ví dụ ["08:00","12:00","20:00"].</summary>
    public List<string> TimeSlots { get; set; } = ["08:00", "12:00", "20:00"];
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";

    /// <summary>
    /// Lệch ngẫu nhiên ± phút quanh mỗi khung giờ, để không đăng khít cùng một phút mỗi ngày
    /// (dấu vết đăng tự động). 0 = tắt, đăng đúng khung giờ.
    /// </summary>
    public int JitterMinutes { get; set; } = 10;
}

public class BulkOperationResult
{
    public int Affected { get; set; }
    public int Skipped { get; set; }
    public List<Guid> PostIds { get; set; } = [];
    public string? Message { get; set; }
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
    public Guid? TextTemplateId { get; set; }
    public Guid? ImageTemplateId { get; set; }
    /// <summary>Id gói danh mục template (thường = TextTemplateId).</summary>
    public Guid? PromptTemplateId { get; set; }
    /// <summary>Tên danh mục template dùng khi sinh bài.</summary>
    public string? PromptTemplateName { get; set; }
    public PostStatus Status { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Lô tạo hàng loạt sinh ra bài này — để UI quay lại trang rải lịch của lô.</summary>
    public Guid? BatchId { get; set; }
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
