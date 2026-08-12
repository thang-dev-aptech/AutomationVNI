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

    /// <summary>
    /// Số ảnh dùng cho flow Template/Reels — null hoặc 1 = 1 ảnh (hành vi cũ). Template: &gt;=3 để bật
    /// chế độ nhiều ảnh (ra bài multi-photo). Reels: số khung hình mong muốn (mặc định lấy theo
    /// ReelsOptions.DefaultFrameCount nếu null).
    /// </summary>
    public int? ImageCount { get; set; }

    /// <summary>Override template prompt cho riêng bài này (thư viện PromptTemplate). No FK.</summary>
    public Guid? TextTemplateId { get; set; }
    public Guid? ImageTemplateId { get; set; }
    /// <summary>Nhóm các bài tạo cùng một lần bulk (fan-out/CSV/AI ideas). No FK.</summary>
    public Guid? BatchId { get; set; }

    /// <summary>Id bài viết gốc nếu bài này được tạo từ chức năng Recycle. Null nếu là bài tạo mới hoàn toàn. No FK.</summary>
    public Guid? SourcePostId { get; set; }

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
