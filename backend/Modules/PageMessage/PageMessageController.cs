using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.PageMessage;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PageMessageController(PageMessageService service) : ControllerBase
{
    [HttpPost("filter")]
    public async Task<IActionResult> Filter(
        [FromBody] PageConversationFilterRequest request,
        CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.FilterAsync(request, ct)));

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.SummaryAsync(ct)));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var data = await service.GetAsync(id, ct);
        return data is null
            ? NotFound(ApiResponse.Fail("NOT_FOUND", "Hội thoại không tồn tại"))
            : Ok(ApiResponse.Ok(data));
    }

    [HttpPost("sync")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Sync(
        [FromBody] SyncPageMessagesRequest request,
        CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.SyncAsync(request, ct), "Đồng bộ Messenger xong"));

    [HttpPost("subscribe-facebook")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Subscribe(CancellationToken ct)
    {
        await service.SubscribeFacebookPagesAsync(ct);
        return Ok(ApiResponse.Ok("Đã đăng ký webhook comment và Messenger cho Facebook Pages"));
    }

    [HttpPost("{id:guid}/send")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Send(
        Guid id,
        [FromBody] SendPageMessageRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await service.SendAsync(id, request, ct), "Đã gửi tin nhắn"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail("MESSAGE_SEND_FAILED", ex.Message));
        }
    }

    [HttpPost("{id:guid}/status")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Status(
        Guid id,
        [FromBody] SetMessageStatusRequest request,
        CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.SetStatusAsync(id, request.Status, ct)));

    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Assign(
        Guid id,
        [FromBody] AssignMessageRequest request,
        CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.AssignAsync(id, request.AssignedTo, ct)));

    [HttpPost("{id:guid}/note")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Note(
        Guid id,
        [FromBody] MessageNoteRequest request,
        CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.NoteAsync(id, request.Note, ct)));
}
