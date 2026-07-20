using Backend.Modules.SocialChannel.Enums;
using Microsoft.Extensions.Options;

namespace Backend.Shared.SocialPublish;

public class SocialPublishService(
    IOptions<SocialPublishOptions> options,
    MockSocialPublishService mockService,
    FacebookPagePublishService facebookService,
    ThreadsPublishService threadsService,
    ILogger<SocialPublishService> logger) : ISocialPublishService
{
    public async Task<SocialPublishResult> PublishAsync(
        SocialPublishRequest request, CancellationToken ct = default)
    {
        var mode = ResolvePublishMode(request);

        return mode switch
        {
            PublishMode.Mock => await mockService.PublishAsync(request, ct),
            PublishMode.RealFacebook => await facebookService.PublishAsync(request, ct),
            PublishMode.RealThreads => await threadsService.PublishAsync(request, ct),
            PublishMode.FailMissingToken => SocialPublishResult.Failed(
                request.Platform == SocialPlatform.Threads ? "THREADS_TOKEN_MISSING" : "FB_TOKEN_MISSING",
                "Social channel has no access token configured."),
            _ => await mockService.PublishAsync(request, ct)
        };
    }

    private PublishMode ResolvePublishMode(SocialPublishRequest request)
    {
        // Mỗi nền tảng có cờ bật riêng — bật Facebook thật không kéo theo Threads và ngược lại.
        var wantsReal = request.ForceReal || request.Platform switch
        {
            SocialPlatform.Facebook => options.Value.UseRealFacebook,
            SocialPlatform.Threads => options.Value.UseRealThreads,
            _ => false
        };

        if (!wantsReal)
            return PublishMode.Mock;

        if (request.Platform is not (SocialPlatform.Facebook or SocialPlatform.Threads))
        {
            logger.LogInformation(
                "Real publish requested for unsupported platform {Platform} on post {PostId}, using mock",
                request.Platform, request.PostId);
            return PublishMode.Mock;
        }

        if (string.IsNullOrWhiteSpace(request.AccessToken))
            return PublishMode.FailMissingToken;

        if (IsDevOrMockToken(request.AccessToken))
        {
            logger.LogInformation(
                "Dev/mock access token on post {PostId}, skipping real publish",
                request.PostId);
            return PublishMode.Mock;
        }

        // TODO: decrypt AccessToken when encryption service is implemented.
        return request.Platform == SocialPlatform.Threads
            ? PublishMode.RealThreads
            : PublishMode.RealFacebook;
    }

    private static bool IsDevOrMockToken(string token)
    {
        var trimmed = token.Trim();
        return string.Equals(trimmed, SocialChannelTokenConstants.DevEncryptedToken, StringComparison.Ordinal)
            || trimmed.StartsWith("mock_", StringComparison.OrdinalIgnoreCase);
    }

    private enum PublishMode
    {
        Mock,
        RealFacebook,
        RealThreads,
        FailMissingToken
    }
}
