using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.SocialConnection;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,ContentManager,Viewer")]
public class SocialConnectionController(SocialConnectionRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await repository.GetWithChannelsAsync(ct);
        return Ok(ApiResponse.Ok(items));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var all = await repository.GetWithChannelsAsync(ct);
        var item = all.FirstOrDefault(x => x.Id == id);
        if (item is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy kết nối"));
        return Ok(ApiResponse.Ok(item));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Disconnect(Guid id, CancellationToken ct)
    {
        var ok = await repository.SoftDisconnectAsync(id, ct);
        if (!ok)
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy kết nối"));
        return Ok(ApiResponse.Ok("Đã ngắt kết nối tài khoản"));
    }
}
