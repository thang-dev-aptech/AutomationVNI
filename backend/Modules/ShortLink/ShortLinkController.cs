using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.ShortLink;

/// <summary>
/// Chuyển hướng link rút gọn. PHẢI ẩn danh — người bấm là độc giả Facebook, không đăng nhập.
/// Đường dẫn cố ý ngắn (/s/...) vì mỗi ký tự đều ăn vào ngân sách 130 ký tự của bài nền màu.
/// </summary>
[ApiController]
[AllowAnonymous]
public class ShortLinkController(ShortLinkService service, ILogger<ShortLinkController> logger)
    : ControllerBase
{
    [HttpGet("/s/{code}")]
    public async Task<IActionResult> Redirect(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 16)
            return NotFound("Link không tồn tại");

        var link = await service.ResolveAsync(code.Trim(), ct);
        if (link is null)
        {
            logger.LogInformation("Link rút gọn không tìm thấy: {Code}", code);
            return NotFound("Link không tồn tại hoặc đã bị gỡ");
        }

        // 302 chứ không 301: trình duyệt cache 301 vĩnh viễn nên sau này không đếm được lượt bấm
        // và cũng không sửa được đích đến.
        return Redirect(link.TargetUrl);
    }
}
