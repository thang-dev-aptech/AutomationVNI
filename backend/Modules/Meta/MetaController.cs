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
    public IActionResult GetConnectUrl()
    {
        if (!metaOAuth.IsConfigured())
        {
            return BadRequest(ApiResponse.Fail(
                "META_NOT_CONFIGURED",
                "Meta OAuth chưa cấu hình đúng. Set MetaOAuth:AppId và MetaOAuth:AppSecret thật qua user-secrets (không dùng placeholder). RedirectUri phải khớp Meta App: http://localhost:5068/api/meta/callback"));
        }

        var userId = userContext.GetCurrentUserId()
            ?? throw new UnauthorizedAccessException("User not authenticated");

        var userName = userContext.GetCurrentUserName() ?? userId.ToString();
        var url = metaOAuth.BuildConnectUrl(userId, userName);

        return Ok(ApiResponse.Ok(new MetaConnectUrlResponse { Url = url }));
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
                "Meta sync complete: {Fb} page(s), {Ig} Instagram, {Gr} group(s)",
                result.FacebookPagesSynced, result.InstagramAccountsSynced, result.FacebookGroupsSynced);

            var redirect = o.FrontendSuccessUri;
            redirect += redirect.Contains('?') ? '&' : '?';
            redirect += $"fb={result.FacebookPagesSynced}&ig={result.InstagramAccountsSynced}&gr={result.FacebookGroupsSynced}";
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
