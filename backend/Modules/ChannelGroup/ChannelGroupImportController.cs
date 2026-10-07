using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.ChannelGroup;

/// <summary>
/// Endpoint nhập CSV nhóm kênh (preview / commit / template).
/// Tách file khỏi ChannelGroupController để không đụng ownership t1.
/// </summary>
[ApiController]
[Route("api/ChannelGroup")]
[Authorize]
public class ChannelGroupImportController(ChannelGroupImportService importService) : ControllerBase
{
    [HttpGet("import/template")]
    [Authorize(Roles = "Admin,ContentManager")]
    public IActionResult DownloadTemplate()
    {
        var bytes = importService.GetTemplateBytes();
        return File(bytes, "text/csv; charset=utf-8", "nhom-kenh-mau.csv");
    }

    [HttpPost("import/preview")]
    [Consumes("multipart/form-data")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Preview(
        [FromForm] IFormFile? file,
        [FromForm] string? mode,
        CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "File CSV không hợp lệ hoặc rỗng"));
        if (!ChannelGroupImportModes.IsValid(mode))
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "mode phải là merge hoặc replace"));

        var result = await importService.PreviewAsync(file, mode!, ct);
        return Ok(ApiResponse.Ok(result));
    }

    [HttpPost("import/commit")]
    [Consumes("multipart/form-data")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Commit(
        [FromForm] IFormFile? file,
        [FromForm] string? mode,
        CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "File CSV không hợp lệ hoặc rỗng"));
        if (!ChannelGroupImportModes.IsValid(mode))
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "mode phải là merge hoặc replace"));

        var result = await importService.CommitAsync(file, mode!, ct);
        return Ok(ApiResponse.Ok(result, $"Nhập xong: tạo {result.GroupsCreated}, cập nhật {result.GroupsUpdated}"));
    }
}
