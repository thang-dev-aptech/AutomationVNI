using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Crm.Opportunities;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CrmOpportunityController(CrmOpportunityService service) : ControllerBase
{
    [HttpPost("filter")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> Filter(
        [FromBody] CrmOpportunityFilterRequest request, CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.FilterAsync(request, ct)));

    [HttpPost("stats")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> Stats(
        [FromBody] CrmOpportunityFilterRequest request, CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.StatsAsync(request, ct)));

    [HttpPost("pipeline")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> Pipeline(
        [FromBody] CrmOpportunityPipelineRequest request, CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.PipelineAsync(request, ct)));

    [HttpPost("pipeline/{stageId:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> PipelineStage(
        Guid stageId, [FromBody] CrmOpportunityPipelinePageRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await service.PipelineStagePageAsync(stageId, request, ct)));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var data = await service.GetAsync(id, ct);
        return data is null
            ? NotFound(ApiResponse.Fail("NOT_FOUND", "Cơ hội không tồn tại"))
            : Ok(ApiResponse.Ok(data));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Create(
        [FromBody] CreateCrmOpportunityRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await service.CreateAsync(request, ct), "Đã tạo cơ hội"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("OPP_INVALID", ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateCrmOpportunityRequest request, CancellationToken ct)
    {
        try
        {
            var data = await service.UpdateAsync(id, request, ct);
            return data is null
                ? NotFound(ApiResponse.Fail("NOT_FOUND", "Cơ hội không tồn tại"))
                : Ok(ApiResponse.Ok(data, "Đã cập nhật"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("OPP_INVALID", ex.Message));
        }
    }

    [HttpPost("{id:guid}/move-stage")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> MoveStage(
        Guid id, [FromBody] MoveCrmOpportunityStageRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await service.MoveStageAsync(id, request, ct), "Đã chuyển giai đoạn"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("OPP_INVALID", ex.Message));
        }
    }

    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Assign(
        Guid id, [FromBody] AssignCrmOpportunityRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await service.AssignAsync(id, request, ct), "Đã giao phụ trách"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpPost("{id:guid}/watchers/{userId:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> AddWatcher(Guid id, Guid userId, CancellationToken ct)
    {
        try
        {
            await service.AddWatcherAsync(id, userId, ct);
            return Ok(ApiResponse.Ok("Đã thêm người theo dõi"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpDelete("{id:guid}/watchers/{userId:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> RemoveWatcher(Guid id, Guid userId, CancellationToken ct)
    {
        try
        {
            await service.RemoveWatcherAsync(id, userId, ct);
            return Ok(ApiResponse.Ok("Đã bỏ người theo dõi"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpPost("{id:guid}/archive")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        try
        {
            await service.ArchiveAsync(id, ct);
            return Ok(ApiResponse.Ok("Đã lưu trữ"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpPost("{id:guid}/unarchive")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken ct)
    {
        try
        {
            await service.UnarchiveAsync(id, ct);
            return Ok(ApiResponse.Ok("Đã bỏ lưu trữ"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("OPP_INVALID", ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await service.SoftDeleteAsync(id, ct);
            return Ok(ApiResponse.Ok("Đã xoá cơ hội"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpPost("from-conversation")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> FromConversation(
        [FromBody] CrmOpportunityFromConversationRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await service.FromConversationAsync(request, ct), "Đã tạo/lấy cơ hội"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("OPP_INVALID", ex.Message));
        }
    }

    [HttpGet("by-conversation/{kind}/{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> ByConversation(string kind, Guid id, CancellationToken ct)
    {
        var data = await service.GetByConversationAsync(kind, id, ct);
        return data is null
            ? Ok(ApiResponse.Ok<object?>(null))
            : Ok(ApiResponse.Ok(data));
    }
}
