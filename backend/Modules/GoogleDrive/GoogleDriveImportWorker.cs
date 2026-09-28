using Backend.Modules.MediaAsset;
using Backend.Shared;
using Microsoft.Extensions.Options;

namespace Backend.Modules.GoogleDrive;

/// <summary>
/// Worker nhập file từ Google Drive (GDRIVE-01). Theo đúng khuôn ContentCrawlWorker: guard
/// Enabled tĩnh + thoát sớm, delay khởi động cho migration/seed, vòng lặp lọc lỗi theo
/// !stoppingToken.IsCancellationRequested (KHÔNG theo loại exception — HttpClient/Drive API
/// timeout cũng ném OperationCanceledException, lọc theo kiểu sẽ vô tình bỏ sót và kéo sập host).
/// </summary>
public class GoogleDriveImportWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<GoogleDriveOptions> options,
    ILogger<GoogleDriveImportWorker> logger) : BackgroundService
{
    private const string ZipExtension = ".zip";

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
                await RunTickAsync(settings, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "GoogleDriveImportWorker lỗi vòng lặp");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(30, settings.IntervalSeconds)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Đọc trạng thái từ DB ở đầu từng tick. Khi dừng, chỉ bỏ qua pha retry+import của tick hiện
    /// tại; vòng lặp và khoảng nghỉ vẫn sống để lần kế tiếp thấy được trạng thái bật lại.
    /// </summary>
    protected virtual async Task RunTickAsync(GoogleDriveOptions settings, CancellationToken ct)
    {
        GoogleDriveSyncStateModel state;
        using (var scope = scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<GoogleDriveRepository>();
            state = await repository.GetSyncStateAsync(ct);
        }

        if (_lastEnabled != state.IsEnabled)
        {
            logger.LogInformation(
                state.IsEnabled ? "Nhập file Google Drive đã bật" : "Nhập file Google Drive đã dừng");
            _lastEnabled = state.IsEnabled;
        }

        if (!state.IsEnabled) return;

        await RetryFailuresAsync(settings, ct);
        await ImportNewFilesAsync(settings, state.PageToken, ct);
    }

    /// <summary>
    /// Thử lại các file đã lỗi trước đó — MỘT scope cho cả pha (DbContext không thread-safe khi
    /// dùng song song, nhưng ở đây tuần tự nên dùng chung vẫn an toàn).
    /// </summary>
    protected virtual async Task RetryFailuresAsync(GoogleDriveOptions settings, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IGoogleDriveClient>();
        var repository = scope.ServiceProvider.GetRequiredService<GoogleDriveRepository>();
        var mediaAssets = scope.ServiceProvider.GetRequiredService<MediaAssetRepository>();

        var failures = await repository.GetRetryableFailuresAsync(
            settings.MaxRetryAttempts, settings.MaxFilesPerTick, ct);

        foreach (var failure in failures)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var data = await client.DownloadFileAsync(failure.GoogleDriveFileId, ct);
                var file = new GoogleDriveFileInfo
                {
                    FileId = failure.GoogleDriveFileId,
                    Name = failure.FileName,
                    MimeType = failure.MimeType,
                    SizeBytes = failure.SizeBytes,
                };
                await mediaAssets.CreateFromGoogleDriveAsync(data, file, ct);
                await repository.DeleteFailureAsync(failure.GoogleDriveFileId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Thử lại file Google Drive {FileId} vẫn lỗi", failure.GoogleDriveFileId);
                await repository.UpsertFailureAsync(
                    failure.GoogleDriveFileId, failure.FileName, failure.MimeType, failure.SizeBytes,
                    ex.Message, ct);
            }
        }
    }

    /// <summary>
    /// Một lượt changes.list. LUÔN advance PageToken ở cuối, kể cả khi không có file mới hoặc
    /// client chưa cấu hình xong — bỏ qua tick chỉ vì thiếu cấu hình không được làm mất pageToken
    /// đã có.
    /// </summary>
    protected virtual async Task ImportNewFilesAsync(
        GoogleDriveOptions settings, string? pageToken, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IGoogleDriveClient>();
        var repository = scope.ServiceProvider.GetRequiredService<GoogleDriveRepository>();
        var mediaAssets = scope.ServiceProvider.GetRequiredService<MediaAssetRepository>();
        var fileStorageOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<FileStorageOptions>>().Value;

        if (!client.IsConfigured())
        {
            logger.LogWarning("GoogleDriveImportWorker chưa cấu hình: {Issue}", client.DescribeConfigIssue());
            return;
        }

        var effectiveToken = string.IsNullOrWhiteSpace(pageToken)
            ? await client.GetStartPageTokenAsync(ct)
            : pageToken;

        // Giới hạn số file mỗi lượt PHẢI xảy ra ở tầng gọi API (tham số maxResults dưới đây), không
        // phải bằng cách Take() sau khi đã nhận nguyên trang — nếu không, NextPageToken lưu lại sẽ
        // khớp với trang ĐẦY ĐỦ trong khi chỉ một phần được xử lý, và phần còn lại mất vĩnh viễn vì
        // pageToken đã đi qua nó rồi (bug đã xảy ra thật, xem review t6 / commit 575a9db).
        var page = await client.ListChangesAsync(effectiveToken, settings.MaxFilesPerTick, ct);
        var importedCount = 0;

        foreach (var file in page.Files)
        {
            ct.ThrowIfCancellationRequested();

            if (await mediaAssets.ExistsByGoogleDriveFileIdAsync(file.FileId, ct))
                continue;

            var extension = Path.GetExtension(file.Name);
            // .zip và mọi đuôi ngoài whitelist bị chặn CÓ CHỦ ĐÍCH ngay ở bước validate — không
            // phải lỗi tải, nên không tạo MediaAsset và không ghi vào hàng đợi retry.
            if (string.Equals(extension, ZipExtension, StringComparison.OrdinalIgnoreCase)
                || !fileStorageOptions.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.SizeBytes > fileStorageOptions.MaxUploadBytes)
            {
                // SaveBytesAsync KHÔNG tự kiểm MaxUploadBytes (chỉ SaveAsync cho IFormFile mới
                // kiểm) — phải so sánh ở đây TRƯỚC khi tải, không thì file khổng lồ vẫn tải trọn
                // về bộ nhớ rồi mới báo lỗi.
                await repository.UpsertFailureAsync(
                    file.FileId, file.Name, file.MimeType, file.SizeBytes,
                    $"File vượt quá giới hạn {fileStorageOptions.MaxUploadBytes} bytes", ct);
                continue;
            }

            try
            {
                var data = await client.DownloadFileAsync(file.FileId, ct);
                await mediaAssets.CreateFromGoogleDriveAsync(data, file, ct);
                importedCount++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Tải file Google Drive {FileId} thất bại", file.FileId);
                await repository.UpsertFailureAsync(
                    file.FileId, file.Name, file.MimeType, file.SizeBytes, ex.Message, ct);
            }
        }

        await repository.UpdateSyncStateAsync(page.NextPageToken, importedCount, ct);
    }
}
