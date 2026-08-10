using System.Text.Json;
using Backend.Data;
using Backend.Modules.ContentCrawl.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.ContentCrawl;

/// <summary>Một nguồn cào ở dạng chuyển được giữa các máy.</summary>
public sealed record PortableSource(
    string Name, CrawlSourceType SourceType, string Url,
    bool IsActive, int MaxItemsPerRun, int LookbackHours,
    List<string>? IncludeKeywords, List<string>? ExcludeKeywords);

public sealed record ImportResult(int Added, int Updated, int Skipped, List<string> Notes);

/// <summary>
/// Xuất / nhập danh sách nguồn cào.
///
/// ═══ VÌ SAO CẦN ═══
///
/// Nguồn cào sống trong CSDL, và <c>.gitignore</c> loại <c>Data/*.db</c>. Nên đẩy code lên VPS
/// là có đủ mọi thứ TRỪ nguồn — máy mới chạy với 0 nguồn, worker quay đều mà không cào gì, và
/// không có lỗi nào để mà đọc.
///
/// Chép cả file CSDL sang cũng được, nhưng thế thì mang theo luôn 400 tin cũ, lịch sử cào, và
/// bảng người dùng của máy dev. Chuyển đúng thứ cần thì sạch hơn.
///
/// CỐ Ý KHÔNG mang theo: LastRunAt, ConsecutiveFailures, LastError, ShortCode. Đó là trạng thái
/// của MÁY CŨ. Mang sang thì máy mới tưởng vừa cào xong và ngồi im tới mốc sau.
/// </summary>
public class CrawlSourcePortability(AppDbContext context, ILogger<CrawlSourcePortability> logger)
{
    public async Task<List<PortableSource>> ExportAsync(CancellationToken ct = default)
        => await context.Set<CrawlSourceModel>()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.Name)
            .Select(x => new PortableSource(
                x.Name, x.SourceType, x.Url, x.IsActive, x.MaxItemsPerRun, x.LookbackHours,
                null, null))
            .ToListAsync(ct);

    /// <summary>
    /// Nhập danh sách nguồn. Khớp theo URL — chạy lại nhiều lần không đẻ thêm bản trùng.
    ///
    /// Nguồn đã có thì CẬP NHẬT tên và cấu hình, KHÔNG đụng lịch sử cào. Người ta chạy lệnh này
    /// lần hai thường là để sửa một nguồn, không phải để xoá sạch những gì máy đích đã cào.
    /// </summary>
    public async Task<ImportResult> ImportAsync(
        IReadOnlyList<PortableSource> sources, CancellationToken ct = default)
    {
        var notes = new List<string>();
        int added = 0, updated = 0, skipped = 0;

        var existing = await context.Set<CrawlSourceModel>()
            .Where(x => !x.IsDeleted)
            .ToDictionaryAsync(x => x.Url, x => x, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var s in sources)
        {
            if (string.IsNullOrWhiteSpace(s.Url) || string.IsNullOrWhiteSpace(s.Name))
            {
                skipped++; notes.Add($"Bỏ qua một mục thiếu tên hoặc địa chỉ"); continue;
            }

            if (existing.TryGetValue(s.Url, out var found))
            {
                found.Name = s.Name;
                found.SourceType = s.SourceType;
                found.IsActive = s.IsActive;
                found.MaxItemsPerRun = s.MaxItemsPerRun;
                found.LookbackHours = s.LookbackHours;
                found.UpdatedAt = DateTime.UtcNow;
                updated++;
                continue;
            }

            context.Set<CrawlSourceModel>().Add(new CrawlSourceModel
            {
                Id = Guid.NewGuid(),
                Name = s.Name,
                SourceType = s.SourceType,
                Url = s.Url,
                IsActive = s.IsActive,
                MaxItemsPerRun = s.MaxItemsPerRun <= 0 ? 15 : s.MaxItemsPerRun,
                LookbackHours = s.LookbackHours <= 0 ? 48 : s.LookbackHours,
                // KHÔNG đặt LastRunAt: để null thì lượt worker đầu tiên cào ngay, đúng thứ người
                // ta mong sau khi vừa nhập nguồn xong.
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "import",
            });
            added++;
        }

        await context.SaveChangesAsync(ct);
        logger.LogInformation("Nhập nguồn cào: thêm {A}, cập nhật {U}, bỏ qua {S}", added, updated, skipped);
        return new ImportResult(added, updated, skipped, notes);
    }

    public static string ToJson(IReadOnlyList<PortableSource> sources)
        => JsonSerializer.Serialize(sources, new JsonSerializerOptions
        {
            WriteIndented = true,
            // camelCase cho khớp phần còn lại của API — file xuất ra dán thẳng vào lệnh nhập
            // được, không phải đổi tên trường.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
}
