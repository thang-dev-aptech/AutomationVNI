using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.Modules.GoogleDrive;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GoogleDriveController(
    GoogleDriveRepository repository,
    GoogleDriveSyncService syncService,
    IOptions<GoogleDriveOptions> options) : ControllerBase
{
    [HttpGet("pipeline-state")]
    public async Task<IActionResult> GetPipelineState(CancellationToken ct)
    {
        var state = await repository.GetSyncStateAsync(ct);
        return Ok(ApiResponse.Ok(ToPipelineStateResponse(state)));
    }

    [HttpPost("pipeline-state")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> SetPipelineState(
        [FromBody] SetGoogleDrivePipelineStateRequest request,
        CancellationToken ct)
    {
        var state = await repository.SetEnabledAsync(
            request.Enabled,
            User.Identity?.Name ?? "unknown",
            ct);
        return Ok(ApiResponse.Ok(ToPipelineStateResponse(state),
            request.Enabled ? "Đã bật nhập file từ Google Drive" : "Đã dừng nhập file từ Google Drive"));
    }

    /// <summary>
    /// GDRIVE-03: quét thủ công ngay lập tức, dùng chung GoogleDriveSyncService (và khoá chung)
    /// với vòng lặp định kỳ của GoogleDriveImportWorker — không bypass IsEnabled, không chạy
    /// chồng lấn với một tick đang chạy.
    /// </summary>
    [HttpPost("scan-now")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> ScanNow(CancellationToken ct)
    {
        var result = await syncService.RunTickAsync(ct);

        if (!result.Enabled)
            return Ok(ApiResponse.Ok(
                ToScanNowResponse(result),
                "Nhập file từ Google Drive đang dừng — bật lên trước khi quét thủ công."));

        if (!result.Configured)
            return Ok(ApiResponse.Ok(
                ToScanNowResponse(result),
                result.ConfigIssue ?? "Google Drive chưa cấu hình xong."));

        return Ok(ApiResponse.Ok(
            ToScanNowResponse(result),
            result.ImportedCount > 0
                ? $"Đã quét xong — nhập {result.ImportedCount} file mới."
                : "Đã quét xong — không có file mới."));
    }

    private static GoogleDrivePipelineStateResponse ToPipelineStateResponse(
        GoogleDriveSyncStateModel state) => new()
        {
            Enabled = state.IsEnabled,
            UpdatedAt = state.UpdatedAt,
            UpdatedByUserName = state.UpdatedByUserName
        };

    private GoogleDriveScanNowResponse ToScanNowResponse(GoogleDriveSyncResult result) => new()
    {
        Enabled = result.Enabled,
        Configured = result.Configured,
        ConfigIssue = result.ConfigIssue,
        ImportedCount = result.ImportedCount,
        MaxFilesPerTick = options.Value.MaxFilesPerTick,
    };
}

public class SetGoogleDrivePipelineStateRequest
{
    public bool Enabled { get; set; }
}

public class GoogleDrivePipelineStateResponse
{
    public bool Enabled { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedByUserName { get; set; }
}

public class GoogleDriveScanNowResponse
{
    public bool Enabled { get; set; }
    public bool Configured { get; set; }
    public string? ConfigIssue { get; set; }
    public int ImportedCount { get; set; }
    public int MaxFilesPerTick { get; set; }
}
