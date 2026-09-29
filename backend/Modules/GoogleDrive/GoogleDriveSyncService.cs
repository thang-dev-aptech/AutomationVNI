using Backend.Modules.MediaAsset;
using Backend.Shared;
using Microsoft.Extensions.Options;

namespace Backend.Modules.GoogleDrive;

/// <summary>Kết quả một lượt RunTickAsync — dùng cho cả worker định kỳ lẫn endpoint scan-now thủ công.</summary>
public record GoogleDriveSyncResult(bool Enabled, bool Configured, string? ConfigIssue, int ImportedCount);

/// <summary>
/// Một lượt quét Google Drive (đọc trạng thái, retry, import) — GDRIVE-03, tách khỏi
/// GoogleDriveImportWorker theo đúng pattern ContentCrawlPipelineService.
/// GDRIVE-05 Task A: tạo/đổi tên/di chuyển-trong-cây MediaFolder theo bản đồ ánh xạ;
/// cascade xoá/reconcile full-tree thuộc task riêng.
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

    private static readonly SemaphoreSlim Lock = new(1, 1);

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

        // GDRIVE-04/05: dedicated folder + dòng ánh xạ root Drive → MediaFolder.
        var dedicatedFolderId = await repository.GetOrCreateDedicatedFolderAsync(settings.FolderId, ct);

        await RetryFailuresAsync(settings, dedicatedFolderId, ct);

        if (!client.IsConfigured())
        {
            var issue = client.DescribeConfigIssue();
            logger.LogWarning("GoogleDriveSyncService chưa cấu hình: {Issue}", issue);
            return new GoogleDriveSyncResult(Enabled: true, Configured: false, ConfigIssue: issue, ImportedCount: 0);
        }

        var importedCount = await ImportNewFilesAsync(settings, state.PageToken, dedicatedFolderId, ct);
        return new GoogleDriveSyncResult(Enabled: true, Configured: true, ConfigIssue: null, ImportedCount: importedCount);
    }

    private async Task RetryFailuresAsync(
        GoogleDriveOptions settings, Guid dedicatedFolderId, CancellationToken ct)
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
                await mediaAssets.CreateFromGoogleDriveAsync(data, file, dedicatedFolderId, ct);
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

    private async Task<int> ImportNewFilesAsync(
        GoogleDriveOptions settings, string? pageToken, Guid dedicatedFolderId, CancellationToken ct)
    {
        var fileStorage = fileStorageOptions.Value;

        var effectiveToken = string.IsNullOrWhiteSpace(pageToken)
            ? await client.GetStartPageTokenAsync(ct)
            : pageToken;

        var page = await client.ListChangesAsync(effectiveToken, settings.MaxFilesPerTick, ct);

        // GetKnownFolderMapAsync tự nâng cấp dòng KnownFolder legacy (MediaFolderId rỗng)
        // về dedicated root — file trong thư mục con GDRIVE-02 cũ không bị bỏ qua âm thầm.
        var map = await repository.GetKnownFolderMapAsync(ct);
        await ReconcileFoldersFromChangesAsync(page.Folders, map, ct);

        var importedCount = 0;

        foreach (var file in page.Files)
        {
            ct.ThrowIfCancellationRequested();

            var targetFolderId = ResolveMappedMediaFolderId(file.Parents, map);
            if (targetFolderId is null)
                continue;

            var existing = await mediaAssets.FindByGoogleDriveFileIdAsync(file.FileId, ct);
            if (existing is not null)
            {
                await mediaAssets.UpdateGoogleDrivePlacementAsync(
                    existing, targetFolderId.Value, file.Name, ct);
                continue;
            }

            var extension = Path.GetExtension(file.Name);
            if (string.Equals(extension, ZipExtension, StringComparison.OrdinalIgnoreCase)
                || !fileStorage.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.SizeBytes > fileStorage.MaxUploadBytes)
            {
                await repository.UpsertFailureAsync(
                    file.FileId, file.Name, file.MimeType, file.SizeBytes,
                    $"File vượt quá giới hạn {fileStorage.MaxUploadBytes} bytes", ct);
                continue;
            }

            try
            {
                var data = await client.DownloadFileAsync(file.FileId, ct);
                await mediaAssets.CreateFromGoogleDriveAsync(data, file, targetFolderId.Value, ct);
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
    /// GDRIVE-05 Task A: hội tụ đa-pass — (a) tạo mới, (b) đổi tên, (c) di chuyển trong cây.
    /// Di chuyển ra ngoài cây (cha mới không trong map) để lại cho Task B cascade.
    /// </summary>
    private async Task ReconcileFoldersFromChangesAsync(
        List<GoogleDriveFolderInfo> folders,
        Dictionary<string, GoogleDriveKnownFolderModel> map,
        CancellationToken ct)
    {
        if (folders.Count == 0) return;

        bool progressed;
        do
        {
            progressed = false;
            foreach (var folder in folders)
            {
                ct.ThrowIfCancellationRequested();
                if (folder.Trashed) continue;
                if (string.IsNullOrWhiteSpace(folder.FolderId)) continue;

                var driveParentId = folder.ParentIds?
                    .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

                if (!map.TryGetValue(folder.FolderId, out var known))
                {
                    // (a) chưa biết — chỉ nhận khi cha đã nằm trong bản đồ.
                    if (driveParentId is null || !map.TryGetValue(driveParentId, out var parentKnown))
                        continue;

                    var created = await repository.CreateMappedChildFolderAsync(
                        folder.FolderId,
                        folder.Name,
                        driveParentId,
                        parentKnown.MediaFolderId,
                        ct);
                    map[created.FolderId] = created;
                    progressed = true;
                    continue;
                }

                // (b) đổi tên
                var desiredName = string.IsNullOrWhiteSpace(folder.Name) ? known.Name : folder.Name.Trim();
                if (!string.Equals(known.Name, desiredName, StringComparison.Ordinal))
                {
                    await repository.RenameMappedMediaFolderAsync(known.MediaFolderId, desiredName, ct);
                    await repository.UpdateFolderMappingAsync(
                        known.FolderId, known.DriveParentId, desiredName, ct);
                    known.Name = desiredName;
                    progressed = true;
                }

                // (c) di chuyển TRONG cây — cha mới phải còn trong map; ngoài cây → bỏ qua (Task B).
                if (driveParentId is not null
                    && !string.Equals(known.DriveParentId, driveParentId, StringComparison.Ordinal)
                    && map.TryGetValue(driveParentId, out var newParent))
                {
                    await repository.ReparentMappedMediaFolderAsync(
                        known.MediaFolderId, newParent.MediaFolderId, ct);
                    await repository.UpdateFolderMappingAsync(
                        known.FolderId, driveParentId, known.Name, ct);
                    known.DriveParentId = driveParentId;
                    progressed = true;
                }
            }
        } while (progressed);
    }

    private static Guid? ResolveMappedMediaFolderId(
        List<string>? parents, Dictionary<string, GoogleDriveKnownFolderModel> map)
    {
        if (parents is null || parents.Count == 0) return null;
        foreach (var parent in parents)
        {
            if (map.TryGetValue(parent, out var known))
                return known.MediaFolderId;
        }
        return null;
    }
}
