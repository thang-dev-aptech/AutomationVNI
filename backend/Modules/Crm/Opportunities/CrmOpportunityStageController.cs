using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Crm.Opportunities;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CrmOpportunityStageController(CrmOpportunityStageService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.ListAsync(ct)));

    [HttpPost]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Create(
        [FromBody] CreateCrmOpportunityStageRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await service.CreateAsync(request, ct), "Đã tạo giai đoạn"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("STAGE_INVALID", ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateCrmOpportunityStageRequest request, CancellationToken ct)
    {
        try
        {
            var data = await service.UpdateAsync(id, request, ct);
            return data is null
                ? NotFound(ApiResponse.Fail("NOT_FOUND", "Giai đoạn không tồn tại"))
                : Ok(ApiResponse.Ok(data, "Đã cập nhật giai đoạn"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("STAGE_INVALID", ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await service.SoftDeleteAsync(id, ct);
            return Ok(ApiResponse.Ok("Đã xoá giai đoạn"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("STAGE_INVALID", ex.Message));
        }
    }

    [HttpPost("reorder")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Reorder(
        [FromBody] ReorderCrmOpportunityStagesRequest request, CancellationToken ct)
    {
        try
        {
            await service.ReorderAsync(request, ct);
            return Ok(ApiResponse.Ok("Đã sắp xếp giai đoạn"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("STAGE_INVALID", ex.Message));
        }
    }
}
