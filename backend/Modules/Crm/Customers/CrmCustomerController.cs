using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Crm.Customers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CrmCustomerController(CrmCustomerService service) : ControllerBase
{
    [HttpPost("filter")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> Filter(
        [FromBody] CrmCustomerFilterRequest request, CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.FilterAsync(request, ct)));

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var data = await service.GetAsync(id, ct);
        return data is null
            ? NotFound(ApiResponse.Fail("NOT_FOUND", "Khách không tồn tại"))
            : Ok(ApiResponse.Ok(data));
    }

    [HttpGet("merge-suggestions")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> MergeSuggestions(CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.ListMergeSuggestionsAsync(ct)));

    [HttpPost("{keptId:guid}/merge")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Merge(
        Guid keptId, [FromBody] MergeCustomersRequest request, CancellationToken ct)
    {
        try
        {
            var result = await service.MergeAsync(keptId, request.SourceCustomerId, ct);
            return Ok(ApiResponse.Ok(result, "Đã gộp khách"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("MERGE_INVALID", ex.Message));
        }
    }

    [HttpPost("merge/{mergeRecordId:guid}/split")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Split(Guid mergeRecordId, CancellationToken ct)
    {
        try
        {
            await service.SplitAsync(mergeRecordId, ct);
            return Ok(ApiResponse.Ok("Đã tách lại khách"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("SPLIT_INVALID", ex.Message));
        }
    }

    [HttpPost("{id:guid}/phone/confirm")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> ConfirmPhone(
        Guid id, [FromBody] ConfirmPhoneRequest request, CancellationToken ct)
    {
        try
        {
            await service.ConfirmPhoneAsync(id, request, ct);
            return Ok(ApiResponse.Ok("Đã xác nhận số điện thoại"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("PHONE_INVALID", ex.Message));
        }
    }

    [HttpPost("{id:guid}/tags")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> AttachTag(
        Guid id, [FromBody] AttachCustomerTagRequest request, CancellationToken ct)
    {
        try
        {
            await service.AttachTagAsync(id, request.TagId, ct);
            return Ok(ApiResponse.Ok("Đã gắn tag"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    /// <summary>Backfill idempotent: tạo/gắn khách từ hội thoại + bình luận cũ.</summary>
    [HttpPost("backfill")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Backfill(CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.BackfillAsync(ct), "Đã backfill khách"));
}
