using Backend.Shared;
using Backend.Shared.Meta;
using Backend.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Meta;

[ApiController]
[Route("api/[controller]")]
public class MetaController(
    IMetaOAuthService metaOAuth,
    IUserContext userContext,
    IOptions<MetaOAuthOptions> options,
    ILogger<MetaController> logger) : ControllerBase
{
    [HttpGet("connect-url")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> GetConnectUrl(CancellationToken ct)
    {
        // Live preflight: verify Meta recognizes the App ID/Secret before sending the user to a
        // dialog that would otherwise fail with the opaque "Nội dung này hiện không hiển thị".
        var configIssue = await metaOAuth.DescribeLiveConfigIssueAsync(ct);
        if (configIssue is not null)
        {
            logger.LogWarning("Meta connect-url rejected: {Issue}", configIssue);
            return BadRequest(ApiResponse.Fail("META_NOT_CONFIGURED", configIssue));
        }

        var userId = userContext.GetCurrentUserId()
            ?? throw new UnauthorizedAccessException("User not authenticated");

        var userName = userContext.GetCurrentUserName() ?? userId.ToString();
        var url = metaOAuth.BuildConnectUrl(userId, userName);
        var o = options.Value;
        var hasConfigId = !string.IsNullOrWhiteSpace(o.ConfigId);

        return Ok(ApiResponse.Ok(new MetaConnectUrlResponse
        {
            Url = url,
            Mode = hasConfigId ? "business" : "classic",
            Hint = hasConfigId
                ? "Đang dùng Login for Business (Config ID)."
                : "Facebook Login cổ điển: khi Connect hãy chọn Page cần cấp quyền. App Development mode thì tài khoản FB phải là Admin/Developer/Tester của app."
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
            logger.LogWarning("Meta OAuth denied: {Error}", error);
            var msg = Uri.EscapeDataString(errorDescription ?? error);
            return Redirect($"{o.FrontendErrorUri}&message={msg}");
        }

        try
        {
            var result = await metaOAuth.HandleCallbackAsync(code!, state!, ct);
            logger.LogInformation(
                "Meta sync complete: {Fb} page(s), {Ig} Instagram, {Gr} group(s), removed={Removed}, returned={Returned}, missingToken={Missing}, granted=[{Granted}]",
                result.FacebookPagesSynced, result.InstagramAccountsSynced, result.FacebookGroupsSynced,
                result.ChannelsRemoved, result.PagesReturnedByMeta, result.PagesMissingToken,
                result.GrantedPermissions);

            var redirect = o.FrontendSuccessUri;
            redirect += redirect.Contains('?') ? '&' : '?';
            redirect += $"fb={result.FacebookPagesSynced}&ig={result.InstagramAccountsSynced}&gr={result.FacebookGroupsSynced}&removed={result.ChannelsRemoved}" +
                        $"&returned={result.PagesReturnedByMeta}&missingToken={result.PagesMissingToken}";
            if (!string.IsNullOrWhiteSpace(result.GrantedPermissions))
                redirect += $"&perms={Uri.EscapeDataString(result.GrantedPermissions)}";
            return Redirect(redirect);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Meta OAuth callback failed");
            var msg = Uri.EscapeDataString(ex.Message);
            return Redirect($"{o.FrontendErrorUri}&message={msg}");
        }
    }
}
