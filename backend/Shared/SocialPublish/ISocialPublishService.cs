namespace Backend.Shared.SocialPublish;

public interface ISocialPublishService
{
    Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default);
}
