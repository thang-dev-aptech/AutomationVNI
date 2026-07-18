using Backend.Modules.Post.Enums;
using Backend.Shared;

namespace Backend.Modules.Post;

public class PostModel : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid SocialChannelId { get; set; }
    public GenerationFlow GenerationFlow { get; set; } = GenerationFlow.FullAI;

    /// <summary>Override template prompt cho riêng bài này (thư viện PromptTemplate). No FK.</summary>
    public Guid? TextTemplateId { get; set; }
    public Guid? ImageTemplateId { get; set; }

    /// <summary>Nhóm các bài tạo cùng một lần bulk (fan-out/CSV/AI ideas). No FK.</summary>
    public Guid? BatchId { get; set; }

    public PostStatus Status { get; set; } = PostStatus.Draft;
    public DateTime? ScheduledPublishAt { get; set; }
    public string? ScheduleTimezone { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? ExternalPostId { get; set; }
    public string? PublishedUrl { get; set; }
    public string? GenerationError { get; set; }
    public string? RejectionReason { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid UserId { get; set; }
}
