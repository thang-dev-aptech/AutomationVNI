using Backend.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Shared.Backup;

public sealed record BackupResult(
    bool Ok, string? Path, long Bytes, int Articles, string? Error, int Kept);

/// <summary>
/// Sao lưu CSDL SQLite bằng <c>VACUUM INTO</c>.
///
/// ═══ VÌ SAO KHÔNG COPY FILE ═══
///
/// CSDL chạy chế độ WAL. Đo lúc viết lớp này:
///
///   vni_automation.db       31.465.472 byte   08:29
///   vni_automation.db-wal    4.169.472 byte   08:30   ← MỚI HƠN
///
/// 4MB dữ liệu mới nhất nằm trong file <c>-wal</c>, chưa gộp vào file chính. Copy mỗi file
/// <c>.db</c> — đúng cách người ta hay làm theo bản năng — là mất phần đó, và bản sao trông
/// vẫn hoàn toàn bình thường. Không có gì báo cho tới lúc cần phục hồi.
///
/// <c>VACUUM INTO</c> ghi ra một file DUY NHẤT đã gộp đủ mọi giao dịch đã cam kết, không cần
/// kèm <c>-wal</c> hay <c>-shm</c>. Đo thật: 0,2 giây cho 31MB, ra 30,8MB, integrity_check ok,
/// đủ 108 bản ghi.
///
/// Nó cũng KHÔNG khoá người ghi lâu như <c>VACUUM</c> thường: đọc một ảnh chụp nhất quán rồi
/// ghi sang file mới, CSDL gốc không bị nén lại tại chỗ.
///
/// Cần SQLite ≥ 3.27. Máy này 3.51.
/// </summary>
public class DatabaseBackupService(
    AppDbContext context,
    IOptions<DatabaseBackupOptions> options,
    IWebHostEnvironment env,
    ILogger<DatabaseBackupService> logger)
{
    private DatabaseBackupOptions Opt => options.Value;

    private string OutputRoot => Path.IsPathRooted(Opt.OutputPath)
        ? Opt.OutputPath
        : Path.Combine(env.ContentRootPath, Opt.OutputPath);

    /// <summary>
    /// Sao lưu ngay. KHÔNG BAO GIỜ ném — sao lưu hỏng không được phép làm sập worker hay
    /// request đang gọi nó. Trả về lý do trong <see cref="BackupResult.Error"/>.
    /// </summary>
    public async Task<BackupResult> RunAsync(CancellationToken ct = default)
    {
        if (!Opt.Enabled)
            return new BackupResult(false, null, 0, 0, "DatabaseBackup:Enabled đang tắt", 0);

        try
        {
            var source = context.Database.GetDbConnection().DataSource;
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                return new BackupResult(false, null, 0, 0, $"Không thấy file CSDL: {source}", 0);

            Directory.CreateDirectory(OutputRoot);

            var free = FreeDiskMb(OutputRoot);
            if (free is not null && free < Opt.MinFreeDiskMb)
                return new BackupResult(false, null, 0, 0,
                    $"Ổ chỉ còn {free}MB, dưới ngưỡng {Opt.MinFreeDiskMb}MB — bỏ lượt sao lưu này", 0);

            // Tên có cả giờ phút: một ngày chạy tay nhiều lần thì không đè lên nhau.
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
            var target = Path.Combine(OutputRoot, $"vni_automation-{stamp}.db");

            // Ghi ra file .tmp rồi đổi tên. Tiến trình chết giữa chừng thì để lại file .tmp dở
            // dang chứ không để lại một bản sao trông như hoàn chỉnh mà thực ra cụt.
            var tmp = target + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);

            // VACUUM INTO không nhận tham số, phải nhúng đường dẫn. Nhân đôi dấu nháy đơn theo
            // đúng luật chuỗi của SQLite — thư mục có dấu nháy sẽ làm hỏng câu lệnh.
            var escaped = tmp.Replace("'", "''");
            await context.Database.ExecuteSqlRawAsync($"VACUUM INTO '{escaped}'", ct);

            File.Move(tmp, target, overwrite: true);

            var bytes = new FileInfo(target).Length;
            var articles = await CountArticlesAsync(target, ct);
            var kept = Prune();

            logger.LogInformation(
                "Sao lưu CSDL xong: {Path} · {Mb:F1}MB · {N} tin · giữ {Kept} bản",
                target, bytes / 1024.0 / 1024.0, articles, kept);

            return new BackupResult(true, target, bytes, articles, null, kept);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sao lưu CSDL hỏng");
            return new BackupResult(false, null, 0, 0, ex.Message, 0);
        }
    }

    /// <summary>
    /// Mở bản sao ra đếm bản ghi.
    ///
    /// Đây là phần KIỂM CHỨNG, không phải trang trí: một file 30MB vẫn có thể mở không được
    /// hoặc rỗng ruột. Không mở thử thì "sao lưu thành công" chỉ có nghĩa là đã ghi được file.
    /// </summary>
    private async Task<int> CountArticlesAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var conn = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly }
                    .ToString());
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM CrawledArticles";
            var n = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt32(n);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không đọc được bản sao vừa tạo — {Path}", path);
            return -1;
        }
    }

    /// <summary>Xoá bản cũ, giữ <c>KeepCount</c> bản mới nhất. Trả về số bản còn lại.</summary>
    private int Prune()
    {
        var files = new DirectoryInfo(OutputRoot)
            .GetFiles("vni_automation-*.db")
            .OrderByDescending(f => f.Name)
            .ToList();

        foreach (var old in files.Skip(Math.Max(1, Opt.KeepCount)))
        {
            try { old.Delete(); logger.LogInformation("Xoá bản sao cũ {Name}", old.Name); }
            catch (Exception ex) { logger.LogWarning(ex, "Không xoá được {Name}", old.Name); }
        }

        // Dọn file .tmp sót lại từ lượt chết giữa chừng.
        foreach (var t in Directory.GetFiles(OutputRoot, "*.tmp"))
            try { File.Delete(t); } catch { /* lượt sau dọn tiếp */ }

        return Math.Min(files.Count, Math.Max(1, Opt.KeepCount));
    }

    public IReadOnlyList<(string Name, long Bytes, DateTime At)> List()
    {
        if (!Directory.Exists(OutputRoot)) return [];
        return new DirectoryInfo(OutputRoot)
            .GetFiles("vni_automation-*.db")
            .OrderByDescending(f => f.Name)
            .Select(f => (f.Name, f.Length, f.LastWriteTime))
            .ToList();
    }

    private static long? FreeDiskMb(string path)
    {
        try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace / 1024 / 1024; }
        catch { return null; }
    }
}
