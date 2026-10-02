using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.MediaCaption;

[ApiController]
[Route("api/[controller]")]
public class MediaCaptionJobController(MediaCaptionJobService service) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Create([FromBody] CreateMediaCaptionJobRequest request, CancellationToken ct)
        => await ExecuteAsync(() => service.CreateAsync(request.FolderId, ct), "Đã tạo caption job");

    [HttpGet]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.ListRecentAsync(ct: ct)));

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => await ExecuteAsync(() => service.GetAsync(id, ct));

    [HttpPost("{id:guid}/retry-failed")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> RetryFailed(Guid id, CancellationToken ct)
        => await ExecuteAsync(async () => { await service.RetryFailedAsync(id, ct); return await service.GetAsync(id, ct); }, "Đã đưa các item lỗi vào hàng chờ");

    [HttpPost("items/{itemId:guid}/retry")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> RetryItem(Guid itemId, CancellationToken ct)
        => await ExecuteAsync(async () => { await service.RetryItemAsync(itemId, ct); return true; }, "Đã đưa item vào hàng chờ");

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action, string? message = null)
    {
        try { return Ok(message is null ? ApiResponse.Ok(await action()) : ApiResponse.Ok(await action(), message)); }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { return BadRequest(ApiResponse.Fail("MEDIA_CAPTION_JOB_FAILED", ex.Message)); }
    }
}
