using Backend.Shared;
using Backend.Shared.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.Modules.NewsSite;

public class SubscribeRequest
{
    public string? Email { get; set; }
}

/// <summary>
/// Mọi thứ CÔNG KHAI của trang tin — độc giả gọi thẳng, không đăng nhập. Tách khỏi
/// <see cref="NewsSiteController"/> (100% admin/reviewer) để không phải rắc <c>[AllowAnonymous]</c>
/// lẻ tẻ vào một controller vốn toàn thao tác quản trị — khuôn theo
/// <c>ShortLinkController</c> (class-level <see cref="AllowAnonymousAttribute"/>).
///
/// Trang tin là site HTML tĩnh, tự nó không gọi được API này (khác domain, không cấu hình CORS).
/// Độc giả bấm trên trang tĩnh → JS gọi 1 file proxy PHP đặt cùng thư mục trang tin → proxy gọi
/// sang đây ở phía SERVER (không bị CORS chặn). Vì vậy các endpoint dưới đây không cần khai CORS.
/// </summary>
[ApiController]
[Route("api/news-public")]
[AllowAnonymous]
public class NewsPublicController(
    NewsSiteRepository repository,
    IEmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    IOptions<NewsSiteOptions> newsOptions,
    ILogger<NewsPublicController> logger) : ControllerBase
{
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int take, CancellationToken ct)
    {
        var results = await repository.SearchPublishedAsync(q ?? "", take <= 0 ? 20 : take, ct);
        return Ok(ApiResponse.Ok(results.Select(a => (object)new
        {
            a.Slug,
            a.Title,
            a.Sapo,
            a.ImageUrl,
            a.CategorySlug,
            a.PublishedAt,
        }).ToList()));
    }

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest? request, CancellationToken ct)
    {
        var email = request?.Email?.Trim() ?? "";
        if (!IsValidEmail(email))
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Email không hợp lệ"));

        var sub = await repository.SubscribeAsync(email, ct);
        logger.LogInformation("Đăng ký nhận tin mới: {Email}", email);

        // Lỗi gửi mail KHÔNG được làm hỏng response đăng ký — người dùng đã ghi DB thành công
        // rồi, mail chào mừng chỉ là tiện ích thêm (cùng tinh thần NewsletterSendWorker).
        if (emailSender.IsConfigured())
        {
            try
            {
                await emailSender.SendAsync(
                    sub.Email, $"Đã đăng ký nhận tin từ {newsOptions.Value.SiteName}",
                    BuildWelcomeHtml(sub.UnsubscribeToken), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Gửi email chào mừng thất bại: {Email}", sub.Email);
            }
        }

        return Ok(ApiResponse.Ok(new { email }, "Đã đăng ký thành công"));
    }

    private string BuildWelcomeHtml(string unsubscribeToken)
    {
        var apiBase = emailOptions.Value.PublicApiBaseUrl.TrimEnd('/');
        var unsubscribeUrl = $"{apiBase}/api/news-public/unsubscribe?token={unsubscribeToken}";
        var siteName = NewsHtml.Esc(newsOptions.Value.SiteName);

        return $"""
            <div style="font-family:system-ui,sans-serif;max-width:560px;margin:0 auto">
              <h2 style="margin-bottom:8px">Đăng ký thành công!</h2>
              <p style="color:#555">
                Bạn sẽ nhận được email mỗi khi {siteName} có bài viết mới — không cần làm gì thêm.
              </p>
              <hr style="margin:24px 0;border:none;border-top:1px solid #eee">
              <p style="font-size:12px;color:#999">
                Không phải bạn đăng ký? <a href="{NewsHtml.Esc(unsubscribeUrl)}" style="color:#999">Huỷ đăng ký</a>.
              </p>
            </div>
            """;
    }

    /// <summary>
    /// Người dùng bấm THẲNG từ email, không qua trang tin/proxy PHP — trả HTML nhỏ gọn trực
    /// tiếp là đủ, không cần khớp pixel-perfect giao diện trang tin.
    /// </summary>
    [HttpGet("unsubscribe")]
    public async Task<ContentResult> Unsubscribe([FromQuery] string? token, CancellationToken ct)
    {
        var ok = !string.IsNullOrWhiteSpace(token) && await repository.UnsubscribeAsync(token, ct);
        var message = ok ? "Đã huỷ đăng ký nhận tin." : "Link không hợp lệ hoặc đã được dùng trước đó.";
        return Content(SimplePage(message), "text/html; charset=utf-8");
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320) return false;
        try { _ = new System.Net.Mail.MailAddress(email); return true; }
        catch (FormatException) { return false; }
    }

    private static string SimplePage(string message) =>
        "<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\">"
        + "<title>VNI Education</title>"
        + "<style>body{font-family:system-ui,sans-serif;max-width:480px;margin:80px auto;text-align:center;color:#1a1a1a}</style>"
        + $"</head><body><h1>{NewsHtml.Esc(message)}</h1></body></html>";
}
