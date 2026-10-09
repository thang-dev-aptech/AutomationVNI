using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Crm.ScheduledMessages;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CrmScheduledMessageController(CrmScheduledMessageService service) : ControllerBase
{
    [HttpGet("by-conversation/{conversationId:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> ByConversation(Guid conversationId, CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.ListByConversationAsync(conversationId, ct)));

    [HttpPost]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Create([FromBody] CreateCrmScheduledMessageRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await service.CreateAsync(request, ct), "Đã hẹn giờ gửi tin"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("SCHEDULED_MESSAGE_INVALID", ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateCrmScheduledMessageRequest request, CancellationToken ct)
    {
        try
        {
            var updated = await service.UpdateAsync(id, request, ct);
            return updated is null
                ? NotFound(ApiResponse.Fail("NOT_FOUND", "Tin hẹn giờ không tồn tại"))
                : Ok(ApiResponse.Ok(updated, "Đã cập nhật tin hẹn giờ"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("SCHEDULED_MESSAGE_INVALID", ex.Message));
        }
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            await service.CancelAsync(id, ct);
            return Ok(ApiResponse.Ok("Đã huỷ tin hẹn giờ"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("SCHEDULED_MESSAGE_INVALID", ex.Message));
        }
    }
}
