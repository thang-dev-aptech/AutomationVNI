using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Crm.Inbox;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CrmInboxController(CrmInboxService service) : ControllerBase
{
    /// <summary>Danh sách hợp nhất tin nhắn + bình luận. Viewer được đọc.</summary>
    [HttpPost("filter")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> Filter(
        [FromBody] CrmInboxFilterRequest request,
        CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.FilterAsync(request, ct)));

    [HttpGet("message/{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> GetMessage(Guid id, CancellationToken ct)
    {
        var data = await service.GetMessageAsync(id, ct);
        return data is null
            ? NotFound(ApiResponse.Fail("NOT_FOUND", "Hội thoại không tồn tại"))
            : Ok(ApiResponse.Ok(data));
    }

    [HttpGet("comment/{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> GetComment(Guid id, CancellationToken ct)
    {
        var data = await service.GetCommentAsync(id, ct);
        return data is null
            ? NotFound(ApiResponse.Fail("NOT_FOUND", "Luồng bình luận không tồn tại"))
            : Ok(ApiResponse.Ok(data));
    }
}
