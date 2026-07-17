using Backend.Modules.PublishLog;

namespace Backend.Shared.SocialPublish;

public class MockSocialPublishService
{
    public Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default)
    {
        var (externalId, publishedUrl, responseJson) = MockPublishResult.Create(request.PublishLogId);
        return Task.FromResult(SocialPublishResult.Succeeded(externalId, publishedUrl, responseJson, usedMock: true));
    }
}
