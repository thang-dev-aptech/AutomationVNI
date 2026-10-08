using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Inbox;

/// <summary>
/// AI gợi ý trả lời (chỉ bản nháp). Route api/Inbox — endpoint duy nhất còn lại trên module này.
/// </summary>
[ApiController]
[Route("api/Inbox")]
[Authorize]
public class InboxSuggestController(InboxSuggestService service) : ControllerBase
{
    [HttpPost("{kind}/{id:guid}/suggest-reply")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> SuggestReply(
        string kind,
        Guid id,
        CancellationToken ct)
    {
        if (!TryParseKind(kind, out var inboxKind))
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Hội thoại không tồn tại"));

        try
        {
            var data = await service.SuggestReplyAsync(inboxKind, id, ct);
            return data is null
                ? NotFound(ApiResponse.Fail("NOT_FOUND", "Hội thoại không tồn tại"))
                : Ok(ApiResponse.Ok(data));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("AI_SUGGEST_FAILED", ex.Message));
        }
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
