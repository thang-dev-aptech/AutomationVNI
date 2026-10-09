using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Crm.Reminders;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CrmReminderController(CrmReminderService service) : ControllerBase
{
    /// <summary>Hôm nay / Quá hạn / Sắp tới (giờ VN). Mặc định lọc theo người đăng nhập.</summary>
    [HttpGet("buckets")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> Buckets(
        [FromQuery] Guid? assigneeUserId,
        [FromQuery] bool all = false,
        CancellationToken ct = default)
    {
        Guid? filter = all ? null : (assigneeUserId ?? GetUserId());
        return Ok(ApiResponse.Ok(await service.ListBucketsAsync(filter, ct)));
    }

    [HttpGet("by-customer/{customerId:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> ByCustomer(Guid customerId, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await service.ListForCustomerAsync(customerId, ct)));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpGet("by-opportunity/{opportunityId:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> ByOpportunity(Guid opportunityId, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await service.ListForOpportunityAsync(opportunityId, ct)));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpPost]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Create(
        [FromBody] CreateCrmReminderRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await service.CreateAsync(request, ct), "Đã tạo nhắc việc"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("REMINDER_INVALID", ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateCrmReminderRequest request, CancellationToken ct)
    {
        try
        {
            var updated = await service.UpdateAsync(id, request, ct);
            return updated is null
                ? NotFound(ApiResponse.Fail("NOT_FOUND", "Nhắc việc không tồn tại"))
                : Ok(ApiResponse.Ok(updated, "Đã cập nhật nhắc việc"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("REMINDER_INVALID", ex.Message));
        }
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        try
        {
            await service.CompleteAsync(id, ct);
            return Ok(ApiResponse.Ok("Đã hoàn thành nhắc việc"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await service.SoftDeleteAsync(id, ct);
            return Ok(ApiResponse.Ok("Đã xoá nhắc việc"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
