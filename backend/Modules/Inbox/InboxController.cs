using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Inbox;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InboxController(InboxQueryService service) : ControllerBase
{
    /// <summary>
    /// Danh sách hội thoại gộp tin nhắn + bình luận gốc.
    /// Phạm vi = PageMessage/filter ∪ SocialComment/filter (xoá mềm + top-level comment khách).
    /// </summary>
    [HttpPost("filter")]
    public async Task<IActionResult> Filter(
        [FromBody] InboxFilterRequest request,
        CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.FilterAsync(request, ct)));

    /// <summary>Badge menu: chưa đọc / mới / đang xử lý gộp.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.SummaryAsync(ct)));

    /// <summary>Khung thông tin khách cho hội thoại đang mở. Ngoài phạm vi → 404.</summary>
    [HttpGet("{kind}/{id:guid}/profile")]
    public async Task<IActionResult> Profile(
        string kind,
        Guid id,
        CancellationToken ct)
    {
        if (!TryParseKind(kind, out var inboxKind))
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Hội thoại không tồn tại"));

        var data = await service.GetProfileAsync(inboxKind, id, ct);
        return data is null
            ? NotFound(ApiResponse.Fail("NOT_FOUND", "Hội thoại không tồn tại"))
            : Ok(ApiResponse.Ok(data));
    }

    private static bool TryParseKind(string kind, out InboxItemKind parsed)
    {
        parsed = default;
        if (string.Equals(kind, "message", StringComparison.OrdinalIgnoreCase))
        {
            parsed = InboxItemKind.Message;
            return true;
        }

        if (string.Equals(kind, "comment", StringComparison.OrdinalIgnoreCase))
        {
            parsed = InboxItemKind.Comment;
            return true;
        }

        return false;
    }
}
