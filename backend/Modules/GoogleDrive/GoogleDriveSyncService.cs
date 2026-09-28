using Backend.Modules.MediaAsset;
using Backend.Shared;
using Microsoft.Extensions.Options;

namespace Backend.Modules.GoogleDrive;

/// <summary>Kết quả một lượt RunTickAsync — dùng cho cả worker định kỳ lẫn endpoint scan-now thủ công.</summary>
public record GoogleDriveSyncResult(bool Enabled, bool Configured, string? ConfigIssue, int ImportedCount);

/// <summary>
/// Một lượt quét Google Drive (đọc trạng thái, retry, import) — GDRIVE-03, tách khỏi
/// GoogleDriveImportWorker theo đúng pattern ContentCrawlPipelineService: scoped service thường,
/// KHÔNG kế thừa BackgroundService, constructor nhận thẳng dependency (không tự tạo scope).
///
/// Dùng chung bởi cả GoogleDriveImportWorker (tick định kỳ) và GoogleDriveController.ScanNow
/// (bấm tay). Lock tĩnh (Lock) đảm bảo hai đường gọi không bao giờ chạy chồng lấn lên cùng một
/// dòng GoogleDriveSyncState — bảng chỉ có đúng 1 dòng singleton, chạy chồng lấn sẽ đua nhau ghi
/// đè PageToken/LastImportedCount.
///
/// Logic RunTickAsync/RetryFailuresAsync/ImportNewFilesAsync là nguyên vẹn, chuyển 1:1 từ
/// GoogleDriveImportWorker — đây là refactor thuần tuý (GDRIVE-03), không đổi hành vi GDRIVE-01.
/// </summary>
public class GoogleDriveSyncService(
    IGoogleDriveClient client,
    GoogleDriveRepository repository,
    MediaAssetRepository mediaAssets,
    IOptions<GoogleDriveOptions> options,
    IOptions<FileStorageOptions> fileStorageOptions,
    ILogger<GoogleDriveSyncService> logger)
{
    private const string ZipExtension = ".zip";

    // Static: worker (một scope) và request scan-now (một scope khác) phải tranh CÙNG một khoá,
    // không phải khoá riêng theo từng instance scoped.
    private static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>
    /// Đọc trạng thái từ DB ở đầu MỖI lượt gọi — IsEnabled=false thì bỏ qua toàn bộ pha
    /// retry+import (worker định kỳ vẫn sống, chỉ tick này không làm gì; scan-now thủ công trả
    /// về rõ ràng "đang dừng"). Chỉ một lượt RunTickAsync được chạy tại một thời điểm trên toàn bộ
    /// app — lượt gọi thứ hai (dù từ worker hay từ scan-now) phải CHỜ, không chạy chồng lấn.
    /// </summary>
    public async Task<GoogleDriveSyncResult> RunTickAsync(CancellationToken ct = default)
    {
        await Lock.WaitAsync(ct);
        try
        {
            return await RunTickCoreAsync(ct);
        }
        finally
        {
            Lock.Release();
        }
    }

    private async Task<GoogleDriveSyncResult> RunTickCoreAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var state = await repository.GetSyncStateAsync(ct);

        if (!state.IsEnabled)
            return new GoogleDriveSyncResult(Enabled: false, Configured: true, ConfigIssue: null, ImportedCount: 0);

        await RetryFailuresAsync(settings, ct);

        if (!client.IsConfigured())
        {
            var issue = client.DescribeConfigIssue();
            logger.LogWarning("GoogleDriveSyncService chưa cấu hình: {Issue}", issue);
            return new GoogleDriveSyncResult(Enabled: true, Configured: false, ConfigIssue: issue, ImportedCount: 0);
        }

        var importedCount = await ImportNewFilesAsync(settings, state.PageToken, ct);
        return new GoogleDriveSyncResult(Enabled: true, Configured: true, ConfigIssue: null, ImportedCount: importedCount);
    }

    /// <summary>
    /// Thử lại các file đã lỗi trước đó — cùng scope/DbContext với ImportNewFilesAsync (tuần tự,
    /// không song song, nên dùng chung vẫn an toàn).
    /// </summary>
    private async Task RetryFailuresAsync(GoogleDriveOptions settings, CancellationToken ct)
    {
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
    /// Một lượt changes.list. GDRIVE-02: đầu tick seed root vào tập đã biết, xử lý Folders trước
    /// (đa vòng cho grandchild cùng page), rồi nhận file khi Parents giao tập đã biết.
    /// LUÔN advance PageToken ở cuối, kể cả khi không có file mới.
    /// </summary>
    private async Task<int> ImportNewFilesAsync(GoogleDriveOptions settings, string? pageToken, CancellationToken ct)
    {
        var fileStorage = fileStorageOptions.Value;

        var effectiveToken = string.IsNullOrWhiteSpace(pageToken)
            ? await client.GetStartPageTokenAsync(ct)
            : pageToken;

        // Giới hạn số file mỗi lượt PHẢI xảy ra ở tầng gọi API (tham số maxResults dưới đây), không
        // phải bằng cách Take() sau khi đã nhận nguyên trang — nếu không, NextPageToken lưu lại sẽ
        // khớp với trang ĐẦY ĐỦ trong khi chỉ một phần được xử lý, và phần còn lại mất vĩnh viễn vì
        // pageToken đã đi qua nó rồi (bug đã xảy ra thật, xem review t6 / commit 575a9db).
        var page = await client.ListChangesAsync(effectiveToken, settings.MaxFilesPerTick, ct);

        await repository.EnsureRootFolderKnownAsync(settings.FolderId, ct);
        var known = await repository.GetKnownFolderIdsAsync(ct);
        await AbsorbKnownFoldersAsync(page.Folders, known, ct);

        var importedCount = 0;

        foreach (var file in page.Files)
        {
            ct.ThrowIfCancellationRequested();

            // GDRIVE-02: chỉ nhận file có parent trực tiếp nằm trong cây đã biết.
            if (file.Parents is null || !file.Parents.Any(known.Contains))
                continue;

            if (await mediaAssets.ExistsByGoogleDriveFileIdAsync(file.FileId, ct))
                continue;

            var extension = Path.GetExtension(file.Name);
            // .zip và mọi đuôi ngoài whitelist bị chặn CÓ CHỦ ĐÍCH ngay ở bước validate — không
            // phải lỗi tải, nên không tạo MediaAsset và không ghi vào hàng đợi retry.
            if (string.Equals(extension, ZipExtension, StringComparison.OrdinalIgnoreCase)
                || !fileStorage.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.SizeBytes > fileStorage.MaxUploadBytes)
            {
                // SaveBytesAsync KHÔNG tự kiểm MaxUploadBytes (chỉ SaveAsync cho IFormFile mới
                // kiểm) — phải so sánh ở đây TRƯỚC khi tải, không thì file khổng lồ vẫn tải trọn
                // về bộ nhớ rồi mới báo lỗi.
                await repository.UpsertFailureAsync(
                    file.FileId, file.Name, file.MimeType, file.SizeBytes,
                    $"File vượt quá giới hạn {fileStorage.MaxUploadBytes} bytes", ct);
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
        return importedCount;
    }

    /// <summary>
    /// Mở rộng tập đã biết từ page.Folders: bỏ trashed / ngoài cây; thêm vào HashSet cục bộ ngay
    /// và lặp đến khi ổn định — để thư mục cháu xuất hiện trước cha trong cùng page vẫn nhận đúng.
    /// </summary>
    private async Task AbsorbKnownFoldersAsync(
        List<GoogleDriveFolderInfo> folders, HashSet<string> known, CancellationToken ct)
    {
        if (folders.Count == 0) return;

        bool added;
        do
        {
            added = false;
            foreach (var folder in folders)
            {
                ct.ThrowIfCancellationRequested();
                if (folder.Trashed) continue;
                if (string.IsNullOrWhiteSpace(folder.FolderId)) continue;
                if (known.Contains(folder.FolderId)) continue;
                if (folder.ParentIds is null || !folder.ParentIds.Any(known.Contains)) continue;

                await repository.AddKnownFolderAsync(folder.FolderId, ct);
                known.Add(folder.FolderId);
                added = true;
            }
        } while (added);
    }
}
