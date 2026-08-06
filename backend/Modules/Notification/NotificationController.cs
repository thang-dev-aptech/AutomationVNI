using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Notification;

/// <summary>
/// Nhật ký hoạt động cho chuông trên thanh trên cùng. Mọi role đều xem được — biết người khác
/// vừa làm gì là nhu cầu chung, không phải quyền đặc biệt.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationController(NotificationService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Feed([FromQuery] int size = 30, CancellationToken ct = default)
        => Ok(ApiResponse.Ok(await service.GetFeedAsync(size, ct)));

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await service.MarkAllReadAsync(ct);
        return Ok(ApiResponse.Ok(true, "Đã đánh dấu đã đọc"));
    }
}
