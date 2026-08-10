using Microsoft.Extensions.Options;

namespace Backend.Shared.Backup;

/// <summary>
/// Chạy sao lưu theo chu kỳ.
///
/// Sao lưu MỘT LẦN NGAY khi khởi động, không đợi hết chu kỳ đầu. Máy chạy 24 giờ rồi khởi
/// động lại trước lượt sao lưu đầu tiên thì sẽ không bao giờ có bản nào — và không có gì báo,
/// vì không có bản sao trông y hệt như chưa tới giờ sao lưu.
/// </summary>
public class DatabaseBackupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DatabaseBackupOptions> options,
    ILogger<DatabaseBackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            logger.LogWarning(
                "SAO LƯU CSDL ĐANG TẮT — không có bản sao nào được tạo. "
                + "Chỉ nên tắt khi đã có sao lưu ở tầng hạ tầng.");
            return;
        }

        logger.LogInformation(
            "Sao lưu CSDL: mỗi {H} giờ, giữ {N} bản, thư mục {P}",
            opt.IntervalHours, opt.KeepCount, opt.OutputPath);

        // Chờ migration và seed xong. Sao lưu giữa lúc đang migrate là chụp một CSDL nửa vời.
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var backup = scope.ServiceProvider.GetRequiredService<DatabaseBackupService>();
                var r = await backup.RunAsync(stoppingToken);

                // Sao lưu hỏng phải KÊU TO. Đây là lớp bảo vệ cuối; hỏng âm thầm thì người ta
                // vẫn tin là có bản sao cho tới ngày cần dùng.
                if (!r.Ok)
                    logger.LogError("SAO LƯU CSDL HỎNG — {Error}", r.Error);
                else if (r.Articles <= 0)
                    logger.LogError(
                        "Bản sao {Path} mở ra KHÔNG đọc được bản ghi nào — coi như hỏng", r.Path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Vòng lặp sao lưu lỗi");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(Math.Clamp(opt.IntervalHours, 1, 168)), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }
}
