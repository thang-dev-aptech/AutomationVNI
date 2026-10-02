using Microsoft.Extensions.Options;

namespace Backend.Modules.GoogleDrive;

/// <summary>
/// Vòng lặp hẹn giờ cho GDRIVE-01/GDRIVE-03 — logic một lượt quét (đọc trạng thái, retry, import)
/// nằm ở GoogleDriveSyncService (scoped, dùng chung với endpoint scan-now thủ công). Worker này
/// chỉ còn nhiệm vụ: chờ khởi động, tạo scope mỗi tick, gọi RunTickAsync, log kết quả.
///
/// Theo đúng khuôn ContentCrawlWorker: guard Enabled tĩnh + thoát sớm, delay khởi động cho
/// migration/seed, vòng lặp lọc lỗi theo !stoppingToken.IsCancellationRequested (KHÔNG theo loại
/// exception — HttpClient/Drive API timeout cũng ném OperationCanceledException, lọc theo kiểu sẽ
/// vô tình bỏ sót và kéo sập host).
/// </summary>
public class GoogleDriveImportWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<GoogleDriveOptions> options,
    ILogger<GoogleDriveImportWorker> logger) : BackgroundService
{
    private bool? _lastEnabled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("GoogleDriveImportWorker bị tắt trong cấu hình");
            return;
        }

        logger.LogInformation(
            "GoogleDriveImportWorker chạy (chu kỳ={Interval}s, tối đa {MaxFiles} file/lượt)",
            settings.IntervalSeconds, settings.MaxFilesPerTick);

        // Chờ một nhịp cho migration và seed xong trước khi đụng DB.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunTickAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "GoogleDriveImportWorker lỗi vòng lặp");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(30, settings.IntervalSeconds)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<GoogleDriveSyncService>();
        var result = await syncService.RunTickAsync(ct);

        if (_lastEnabled != result.Enabled)
        {
            logger.LogInformation(
                result.Enabled ? "Nhập file Google Drive đã bật" : "Nhập file Google Drive đã dừng");
            _lastEnabled = result.Enabled;
        }

        if (result.Enabled && !result.Configured)
            logger.LogWarning("GoogleDriveImportWorker chưa cấu hình: {Issue}", result.ConfigIssue);
        else if (result.ImportedCount > 0)
            logger.LogInformation("Đã nhập {Count} file mới từ Google Drive", result.ImportedCount);
    }
}
