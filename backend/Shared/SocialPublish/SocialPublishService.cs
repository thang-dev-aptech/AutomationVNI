using Backend.Modules.SocialChannel.Enums;
using Microsoft.Extensions.Options;

namespace Backend.Shared.SocialPublish;

public class SocialPublishService(
    IOptions<SocialPublishOptions> options,
    MockSocialPublishService mockService,
    FacebookPagePublishService facebookService,
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
            PublishMode.FailMissingToken => SocialPublishResult.Failed(
                "FB_TOKEN_MISSING",
                "Social channel has no Facebook page access token configured."),
            _ => await mockService.PublishAsync(request, ct)
        };
    }

    private PublishMode ResolvePublishMode(SocialPublishRequest request)
    {
        var wantsReal = options.Value.UseRealFacebook || request.ForceReal;

        if (!wantsReal)
            return PublishMode.Mock;

        if (request.Platform != SocialPlatform.Facebook)
        {
            logger.LogInformation(
                "Real publish requested for non-Facebook platform on post {PostId}, using mock",
                request.PostId);
            return PublishMode.Mock;
        }

        if (string.IsNullOrWhiteSpace(request.AccessToken))
            return PublishMode.FailMissingToken;

        if (IsDevOrMockToken(request.AccessToken))
        {
            logger.LogInformation(
                "Dev/mock access token on post {PostId}, skipping real Facebook publish",
                request.PostId);
            return PublishMode.Mock;
        }

        // TODO: decrypt AccessToken when encryption service is implemented.
        return PublishMode.RealFacebook;
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
        FailMissingToken
    }
}
