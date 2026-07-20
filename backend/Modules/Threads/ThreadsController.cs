using Backend.Shared;
using Backend.Shared.Repositories;
using Backend.Shared.Threads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Threads;

[ApiController]
[Route("api/[controller]")]
public class ThreadsController(
    IThreadsOAuthService threadsOAuth,
    IUserContext userContext,
    IOptions<ThreadsOAuthOptions> options,
    ILogger<ThreadsController> logger) : ControllerBase
{
    [HttpGet("connect-url")]
    [Authorize(Roles = "Admin,ContentManager")]
    public IActionResult GetConnectUrl()
    {
        var configIssue = threadsOAuth.DescribeConfigIssue();
        if (configIssue is not null)
        {
            logger.LogWarning("Threads connect-url rejected: {Issue}", configIssue);
            return BadRequest(ApiResponse.Fail("THREADS_NOT_CONFIGURED", configIssue));
        }

        var userId = userContext.GetCurrentUserId()
            ?? throw new UnauthorizedAccessException("User not authenticated");

        var userName = userContext.GetCurrentUserName() ?? userId.ToString();

        return Ok(ApiResponse.Ok(new ThreadsConnectUrlResponse
        {
            Url = threadsOAuth.BuildConnectUrl(userId, userName),
            Hint = "App Development mode: tài khoản Threads phải được mời vào Threads Testers " +
                   "và accept trong app Threads (Settings → Account → Website permissions → Invites). " +
                   "Admin của app cũng phải được mời riêng."
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
            logger.LogWarning("Threads OAuth denied: {Error}", error);
            var msg = Uri.EscapeDataString(errorDescription ?? error);
            return Redirect($"{o.FrontendErrorUri}&message={msg}");
        }

        try
        {
            var result = await threadsOAuth.HandleCallbackAsync(code!, state!, ct);
            logger.LogInformation(
                "Threads sync complete: {Profiles} profile(s) ({Username}), removed={Removed}, expiresAt={ExpiresAt:o}",
                result.ProfilesSynced, result.Username, result.ChannelsRemoved, result.TokenExpiresAt);

            var redirect = o.FrontendSuccessUri;
            redirect += redirect.Contains('?') ? '&' : '?';
            redirect += $"profiles={result.ProfilesSynced}&removed={result.ChannelsRemoved}";
            if (!string.IsNullOrWhiteSpace(result.Username))
                redirect += $"&username={Uri.EscapeDataString(result.Username)}";
            return Redirect(redirect);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Threads OAuth callback failed");
            var msg = Uri.EscapeDataString(ex.Message);
            return Redirect($"{o.FrontendErrorUri}&message={msg}");
        }
    }
}
