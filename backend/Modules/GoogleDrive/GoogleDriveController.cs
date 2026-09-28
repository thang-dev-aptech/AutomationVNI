using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.GoogleDrive;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GoogleDriveController(GoogleDriveRepository repository) : ControllerBase
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

    private static GoogleDrivePipelineStateResponse ToPipelineStateResponse(
        GoogleDriveSyncStateModel state) => new()
        {
            Enabled = state.IsEnabled,
            UpdatedAt = state.UpdatedAt,
            UpdatedByUserName = state.UpdatedByUserName
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
