using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialConnection;
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
    SocialConnectionRepository connectionRepository,
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

    /// <summary>
    /// Uninstall callback (App Dashboard → Threads → "Gỡ cài đặt URL gọi lại").
    /// Threads POSTs a signed_request when a user removes the app; we revoke the stored connection.
    /// </summary>
    [HttpPost("uninstall")]
    [AllowAnonymous]
    public async Task<IActionResult> Uninstall([FromForm(Name = "signed_request")] string? signedRequest, CancellationToken ct)
    {
        var userId = ParseSignedRequestUserId(signedRequest);
        if (userId is null)
            return BadRequest(new { error = "invalid signed_request" });

        var removed = await connectionRepository.SoftDisconnectByExternalUserIdAsync(
            SocialProvider.Threads, userId, "threads-uninstall", ct);
        logger.LogInformation("Threads uninstall callback for user {UserId}, removed={Removed}", userId, removed);
        return Ok();
    }

    /// <summary>
    /// Data deletion callback (App Dashboard → Threads → "Xóa URL gọi lại").
    /// Must answer with a status URL + confirmation code per Meta's data deletion contract.
    /// </summary>
    [HttpPost("delete")]
    [AllowAnonymous]
    public async Task<IActionResult> DataDeletion([FromForm(Name = "signed_request")] string? signedRequest, CancellationToken ct)
    {
        var userId = ParseSignedRequestUserId(signedRequest);
        if (userId is null)
            return BadRequest(new { error = "invalid signed_request" });

        var removed = await connectionRepository.SoftDisconnectByExternalUserIdAsync(
            SocialProvider.Threads, userId, "threads-data-deletion", ct);
        logger.LogInformation("Threads data deletion callback for user {UserId}, removed={Removed}", userId, removed);

        var confirmationCode = $"thr-{userId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        return Ok(new
        {
            url = "https://phattan.xyz/data-deletion",
            confirmation_code = confirmationCode
        });
    }

    /// <summary>
    /// Verifies the HMAC-SHA256 signature of Meta's signed_request against the Threads app secret
    /// and returns the user_id from the payload, or null when missing/invalid.
    /// </summary>
    private string? ParseSignedRequestUserId(string? signedRequest)
    {
        if (string.IsNullOrWhiteSpace(signedRequest)) return null;

        var parts = signedRequest.Split('.', 2);
        if (parts.Length != 2) return null;

        try
        {
            var signature = Base64UrlDecode(parts[0]);
            var payloadBytes = Base64UrlDecode(parts[1]);

            var expected = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(options.Value.AppSecret), Encoding.UTF8.GetBytes(parts[1]));
            if (!CryptographicOperations.FixedTimeEquals(signature, expected))
            {
                logger.LogWarning("Threads signed_request signature mismatch");
                return null;
            }

            using var doc = JsonDocument.Parse(payloadBytes);
            return doc.RootElement.TryGetProperty("user_id", out var idEl)
                ? idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : idEl.GetRawText()
                : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Threads signed_request parse failed");
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
