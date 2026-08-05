namespace Backend.Shared.SocialPublish;

public interface ISocialPublishService
{
    Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default);

    /// <summary>
    /// Đăng bình luận vào bài vừa đăng. Dùng để đặt link nguồn xuống bình luận đầu tiên —
    /// bài nền màu của Facebook chỉ cho 130 ký tự nên nhét URL vào thân bài là hết chỗ viết.
    /// </summary>
    Task<SocialPublishResult> CommentAsync(
        SocialCommentPublishRequest request, CancellationToken ct = default);
}

public class SocialCommentPublishRequest
{
    public Guid PostId { get; set; }
    public Backend.Modules.SocialChannel.Enums.SocialPlatform Platform { get; set; }
    /// <summary>ID bài trên Facebook (dạng {page}_{post}).</summary>
    public string ExternalPostId { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool ForceReal { get; set; }
}
