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

    /// <summary>
    /// Hồ sơ khách cột phải SO9. Viewer được đọc. GET không ghi DB.
    /// kind = message | comment.
    /// </summary>
    [HttpGet("{kind}/{id:guid}/customer")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> GetCustomer(string kind, Guid id, CancellationToken ct)
    {
        if (!TryParseKind(kind, out var parsed))
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Loại hội thoại không hợp lệ"));

        var data = await service.GetCustomerPanelAsync(parsed, id, ct);
        return data is null
            ? NotFound(ApiResponse.Fail("NOT_FOUND", "Hội thoại không tồn tại"))
            : Ok(ApiResponse.Ok(data));
    }

    private static bool TryParseKind(string kind, out CrmInboxItemKind parsed)
    {
        if (string.Equals(kind, "message", StringComparison.OrdinalIgnoreCase))
        {
            parsed = CrmInboxItemKind.Message;
            return true;
        }

        if (string.Equals(kind, "comment", StringComparison.OrdinalIgnoreCase))
        {
            parsed = CrmInboxItemKind.Comment;
            return true;
        }

        parsed = default;
        return false;
    }
}
