using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Crm.Tags;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CrmTagController(CrmTagService service) : ControllerBase
{
    /// <summary>Danh sách tag — Admin/CM/Reviewer (để gắn trên hộp thư).</summary>
    [HttpGet]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.ListAsync(ct)));

    [HttpPost]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Create([FromBody] CreateCrmTagRequest request, CancellationToken ct)
    {
        try
        {
            var created = await service.CreateAsync(request, ct);
            return Ok(ApiResponse.Ok(created, "Đã tạo tag"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("TAG_INVALID", ex.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateCrmTagRequest request, CancellationToken ct)
    {
        try
        {
            var updated = await service.UpdateAsync(id, request, ct);
            return updated is null
                ? NotFound(ApiResponse.Fail("NOT_FOUND", "Tag không tồn tại"))
                : Ok(ApiResponse.Ok(updated, "Đã cập nhật tag"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("TAG_INVALID", ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await service.SoftDeleteAsync(id, ct);
        return ok
            ? Ok(ApiResponse.Ok("Đã xoá tag"))
            : NotFound(ApiResponse.Fail("NOT_FOUND", "Tag không tồn tại"));
    }

    [HttpPost("{id:guid}/attach")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Attach(
        Guid id, [FromBody] CrmTagTargetRequest request, CancellationToken ct)
    {
        try
        {
            await service.AttachAsync(id, request, ct);
            return Ok(ApiResponse.Ok("Đã gắn tag"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpPost("{id:guid}/detach")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Detach(
        Guid id, [FromBody] CrmTagTargetRequest request, CancellationToken ct)
    {
        try
        {
            await service.DetachAsync(id, request, ct);
            return Ok(ApiResponse.Ok("Đã bỏ tag"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }
}
