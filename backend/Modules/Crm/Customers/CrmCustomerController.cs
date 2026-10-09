using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Crm.Customers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CrmCustomerController(
    CrmCustomerService service,
    CrmCustomerCareService care) : ControllerBase
{
    [HttpPost("filter")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> Filter(
        [FromBody] CrmCustomerFilterRequest request, CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.FilterAsync(request, ct)));

    [HttpGet("merge-suggestions")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> MergeSuggestions(CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.ListMergeSuggestionsAsync(ct)));

    [HttpGet("import/template")]
    [Authorize(Roles = "Admin,ContentManager")]
    public IActionResult ImportTemplate()
        => File(care.GetCsvTemplateBytes(), "text/csv; charset=utf-8", "crm-khach-mau.csv");

    [HttpPost("import/preview")]
    [Consumes("multipart/form-data")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> ImportPreview(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "File CSV không hợp lệ"));
        try
        {
            return Ok(ApiResponse.Ok(await care.PreviewCsvAsync(file, ct)));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("CSV_INVALID", ex.Message));
        }
    }

    [HttpPost("import/commit")]
    [Consumes("multipart/form-data")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> ImportCommit(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "File CSV không hợp lệ"));
        try
        {
            return Ok(ApiResponse.Ok(await care.CommitCsvAsync(file, ct), "Đã nhập CSV"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("CSV_INVALID", ex.Message));
        }
    }

    [HttpGet("export")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var (bytes, name) = await care.ExportCsvAsync(ct);
        return File(bytes, "text/csv; charset=utf-8", name);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Create(
        [FromBody] CreateCrmCustomerRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await care.CreateManualAsync(request, ct), "Đã tạo khách"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("CUSTOMER_INVALID", ex.Message));
        }
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var data = await service.GetAsync(id, ct);
        return data is null
            ? NotFound(ApiResponse.Fail("NOT_FOUND", "Khách không tồn tại"))
            : Ok(ApiResponse.Ok(data));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateCrmCustomerRequest request, CancellationToken ct)
    {
        try
        {
            var data = await care.UpdateAsync(id, request, ct);
            return data is null
                ? NotFound(ApiResponse.Fail("NOT_FOUND", "Khách không tồn tại"))
                : Ok(ApiResponse.Ok(data, "Đã cập nhật khách"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("CUSTOMER_INVALID", ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        try
        {
            await care.SoftDeleteAsync(id, ct);
            return Ok(ApiResponse.Ok("Đã xoá mềm khách"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpDelete("{id:guid}/hard")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> HardDelete(Guid id, CancellationToken ct)
    {
        try
        {
            await care.HardDeleteAsync(id, ct);
            return Ok(ApiResponse.Ok("Đã xoá hẳn khách"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpGet("{id:guid}/timeline")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> Timeline(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await care.GetTimelineAsync(id, ct)));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpGet("{id:guid}/notes")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
    public async Task<IActionResult> ListNotes(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await care.ListNotesAsync(id, ct)));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    [HttpPost("{id:guid}/notes")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> AddNote(
        Guid id, [FromBody] CreateCrmCustomerNoteRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await care.AddNoteAsync(id, request, ct), "Đã thêm ghi chú"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("NOTE_INVALID", ex.Message));
        }
    }

    [HttpPut("{id:guid}/notes/{noteId:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> UpdateNote(
        Guid id, Guid noteId, [FromBody] UpdateCrmCustomerNoteRequest request, CancellationToken ct)
    {
        try
        {
            var note = await care.UpdateNoteAsync(id, noteId, request, ct);
            return note is null
                ? NotFound(ApiResponse.Fail("NOT_FOUND", "Ghi chú không tồn tại"))
                : Ok(ApiResponse.Ok(note, "Đã cập nhật ghi chú"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("NOTE_INVALID", ex.Message));
        }
    }

    [HttpDelete("{id:guid}/notes/{noteId:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> DeleteNote(Guid id, Guid noteId, CancellationToken ct)
    {
        try
        {
            await care.SoftDeleteNoteAsync(id, noteId, ct);
            return Ok(ApiResponse.Ok("Đã xoá ghi chú"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

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

    [HttpPost("backfill")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Backfill(CancellationToken ct)
        => Ok(ApiResponse.Ok(await service.BackfillAsync(ct), "Đã backfill khách"));
}
