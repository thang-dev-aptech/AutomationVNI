using Backend.Shared;
using Backend.Shared.Repositories;
using Backend.Shared.TikTok;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.Modules.TikTok;

[ApiController]
[Route("api/[controller]")]
public class TikTokController(
    ITikTokOAuthService tikTokOAuth,
    IUserContext userContext,
    IOptions<TikTokOAuthOptions> options,
    ILogger<TikTokController> logger) : ControllerBase
{
    [HttpGet("connect-url")]
    [Authorize(Roles = "Admin,ContentManager")]
    public IActionResult GetConnectUrl()
    {
        var configIssue = tikTokOAuth.DescribeConfigIssue();
        if (configIssue is not null)
        {
            logger.LogWarning("TikTok connect-url rejected: {Issue}", configIssue);
            return BadRequest(ApiResponse.Fail("TIKTOK_NOT_CONFIGURED", configIssue));
        }

        var userId = userContext.GetCurrentUserId()
            ?? throw new UnauthorizedAccessException("User not authenticated");

        var userName = userContext.GetCurrentUserName() ?? userId.ToString();

        return Ok(ApiResponse.Ok(new TikTokConnectUrlResponse
        {
            Url = tikTokOAuth.BuildConnectUrl(userId, userName),
            Hint = "App chưa qua audit của TikTok chỉ đăng được ở chế độ riêng tư (SELF_ONLY) " +
                   "và tối đa 5 tài khoản uỷ quyền/24h."
        }));
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken ct)
    {
        var o = options.Value;

        if (!string.IsNullOrWhiteSpace(error))
        {
            logger.LogWarning("TikTok OAuth denied: {Error}", error);
            var msg = Uri.EscapeDataString(errorDescription ?? error);
            return Redirect($"{o.FrontendErrorUri}&message={msg}");
        }

        try
        {
            var result = await tikTokOAuth.HandleCallbackAsync(code!, state!, ct);
            logger.LogInformation(
                "TikTok sync complete: {Profiles} profile(s) ({DisplayName}), removed={Removed}, expiresAt={ExpiresAt:o}",
                result.ProfilesSynced, result.DisplayName, result.ChannelsRemoved, result.TokenExpiresAt);

            var redirect = o.FrontendSuccessUri;
            redirect += redirect.Contains('?') ? '&' : '?';
            redirect += $"profiles={result.ProfilesSynced}&removed={result.ChannelsRemoved}";
            if (!string.IsNullOrWhiteSpace(result.DisplayName))
                redirect += $"&username={Uri.EscapeDataString(result.DisplayName)}";
            return Redirect(redirect);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TikTok OAuth callback failed");
            var msg = Uri.EscapeDataString(ex.Message);
            return Redirect($"{o.FrontendErrorUri}&message={msg}");
        }
    }
}
