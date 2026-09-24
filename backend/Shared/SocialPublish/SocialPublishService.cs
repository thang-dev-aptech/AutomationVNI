using Backend.Modules.SocialChannel.Enums;
using Microsoft.Extensions.Options;

namespace Backend.Shared.SocialPublish;

public class SocialPublishService(
    IOptions<SocialPublishOptions> options,
    MockSocialPublishService mockService,
    FacebookPagePublishService facebookService,
    ThreadsPublishService threadsService,
    TikTokPublishService tikTokService,
    ILogger<SocialPublishService> logger) : ISocialPublishService
{
    /// <summary>
    /// Bình luận chỉ chạy thật với Facebook. Chế độ mock trả thành công giả để luồng phía trên
    /// không phải rẽ nhánh — bình luận là phần phụ, không được làm hỏng việc đăng bài.
    /// </summary>
    public async Task<SocialPublishResult> CommentAsync(
        SocialCommentPublishRequest request, CancellationToken ct = default)
    {
        var wantsReal = request.ForceReal || options.Value.UseRealFacebook;
        if (!wantsReal
            || request.Platform != Backend.Modules.SocialChannel.Enums.SocialPlatform.Facebook
            || string.IsNullOrWhiteSpace(request.AccessToken))
        {
            logger.LogInformation(
                "Bỏ qua bình luận link nguồn cho post {PostId} (chế độ mock hoặc nền tảng không hỗ trợ)",
                request.PostId);
            return SocialPublishResult.Succeeded("mock-comment", string.Empty, null, usedMock: true);
        }
        return await facebookService.CommentAsync(request, ct);
    }

    public async Task<SocialPublishResult> PublishAsync(
        SocialPublishRequest request, CancellationToken ct = default)
    {
        var mode = ResolvePublishMode(request);

        return mode switch
        {
            PublishMode.Mock => await mockService.PublishAsync(request, ct),
            PublishMode.RealFacebook => await facebookService.PublishAsync(request, ct),
            PublishMode.RealThreads => await threadsService.PublishAsync(request, ct),
            PublishMode.RealTikTok => await tikTokService.PublishAsync(request, ct),
            PublishMode.FailMissingToken => SocialPublishResult.Failed(
                request.Platform switch
                {
                    SocialPlatform.Threads => "THREADS_TOKEN_MISSING",
                    SocialPlatform.TikTok => "TIKTOK_TOKEN_MISSING",
                    _ => "FB_TOKEN_MISSING"
                },
                "Social channel has no access token configured."),
            _ => await mockService.PublishAsync(request, ct)
        };
    }

    private PublishMode ResolvePublishMode(SocialPublishRequest request)
    {
        // Mỗi nền tảng có cờ bật riêng — bật Facebook thật không kéo theo Threads/TikTok và ngược lại.
        var wantsReal = request.ForceReal || request.Platform switch
        {
            SocialPlatform.Facebook => options.Value.UseRealFacebook,
            SocialPlatform.Threads => options.Value.UseRealThreads,
            SocialPlatform.TikTok => options.Value.UseRealTikTok,
            _ => false
        };

        if (!wantsReal)
            return PublishMode.Mock;

        if (request.Platform is not (SocialPlatform.Facebook or SocialPlatform.Threads or SocialPlatform.TikTok))
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
        return request.Platform switch
        {
            SocialPlatform.Threads => PublishMode.RealThreads,
            SocialPlatform.TikTok => PublishMode.RealTikTok,
            _ => PublishMode.RealFacebook
        };
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
        RealTikTok,
        FailMissingToken
    }
}
