using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Shared.Backup;

/// <summary>
/// Sao lưu CSDL bằng tay và xem các bản đã có.
///
/// Chỉ Admin: bản sao chứa toàn bộ dữ liệu, kể cả bảng người dùng.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class BackupController(DatabaseBackupService backup) : ControllerBase
{
    /// <summary>Danh sách bản sao đang có, mới nhất trước.</summary>
    [HttpGet]
    public IActionResult List()
    {
        var items = backup.List()
            .Select(x => new { x.Name, sizeMb = Math.Round(x.Bytes / 1024.0 / 1024.0, 1), at = x.At })
            .ToList();

        return Ok(ApiResponse.Ok(new
        {
            count = items.Count,
            // Nói thẳng khi chưa có bản nào — đây là trạng thái nguy hiểm, không phải "danh sách rỗng".
            warning = items.Count == 0 ? "CHƯA CÓ BẢN SAO NÀO — dữ liệu đang không được bảo vệ" : null,
            items,
        }));
    }

    /// <summary>Sao lưu ngay. Mất ~0,2 giây cho CSDL 30MB.</summary>
    [HttpPost]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        var r = await backup.RunAsync(ct);
        if (!r.Ok) return BadRequest(ApiResponse.Fail("BACKUP_FAILED", r.Error ?? "Không rõ lý do"));

        return Ok(ApiResponse.Ok(new
        {
            path = r.Path,
            sizeMb = Math.Round(r.Bytes / 1024.0 / 1024.0, 1),
            articles = r.Articles,
            kept = r.Kept,
        }, $"Đã sao lưu {r.Articles} tin, giữ {r.Kept} bản"));
    }
}
