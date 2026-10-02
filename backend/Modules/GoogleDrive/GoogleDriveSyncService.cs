using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Backend.Shared;
using Microsoft.Extensions.Options;

namespace Backend.Modules.GoogleDrive;

/// <summary>Kết quả một lượt RunTickAsync — dùng cho cả worker định kỳ lẫn endpoint scan-now thủ công.</summary>
public record GoogleDriveSyncResult(bool Enabled, bool Configured, string? ConfigIssue, int ImportedCount);

/// <summary>
/// Một lượt quét Google Drive (đọc trạng thái, retry, import) — GDRIVE-03, tách khỏi
/// GoogleDriveImportWorker theo đúng pattern ContentCrawlPipelineService.
/// GDRIVE-05 Task A: tạo/đổi tên/di chuyển-trong-cây MediaFolder theo bản đồ ánh xạ.
/// GDRIVE-05 Task B: cascade soft-delete khi xoá/trash/di chuyển ra ngoài cây, + reconcile toàn
/// cây một lần (di sản GDRIVE-04: file nằm phẳng ở dedicated root được đặt lại đúng thư mục con).
/// </summary>
public class GoogleDriveSyncService(
    IGoogleDriveClient client,
    GoogleDriveRepository repository,
    MediaAssetRepository mediaAssets,
    MediaFolderRepository mediaFolders,
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

        // GDRIVE-05 Task B: quét toàn cây MỘT LẦN (lazy, giống cách GetOrCreateDedicatedFolderAsync
        // tự khởi tạo) — lỗi giữa chừng (mạng/timeout) không được làm hỏng cả tick, chỉ log và thử
        // lại ở tick sau (FullTreeReconciledAt vẫn null cho tới khi thật sự xong).
        if (client.IsConfigured())
        {
            try
            {
                await ReconcileFullTreeOnceAsync(settings.FolderId, dedicatedFolderId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "ReconcileFullTreeOnceAsync lỗi — sẽ thử lại ở tick sau");
            }
        }

        var retriedCount = await RetryFailuresAsync(settings, dedicatedFolderId, ct);

        if (!client.IsConfigured())
        {
            var issue = client.DescribeConfigIssue();
            logger.LogWarning("GoogleDriveSyncService chưa cấu hình: {Issue}", issue);
            return new GoogleDriveSyncResult(Enabled: true, Configured: false, ConfigIssue: issue, ImportedCount: retriedCount);
        }

        var importedCount = await ImportNewFilesAsync(settings, state, dedicatedFolderId, ct);
        return new GoogleDriveSyncResult(Enabled: true, Configured: true, ConfigIssue: null, ImportedCount: importedCount + retriedCount);
    }

    /// <summary>
    /// GDRIVE-05 Task B: quét TOÀN BỘ cây con hiện có từ root Drive folder (không phải delta như
    /// ListChangesAsync) — chạy đúng 1 lần trên toàn vòng đời, gate bằng FullTreeReconciledAt.
    /// Tạo đủ MediaFolder + bản đồ ánh xạ cho MỌI thư mục con hiện có (không chỉ thư mục mới từ
    /// lúc bật tính năng), rồi đặt lại FolderId cho MediaAsset Source=GoogleDrive từng nằm phẳng
    /// ở dedicated root (di sản GDRIVE-04) sang đúng thư mục con thật theo GoogleDriveFileId.
    ///
    /// Idempotent theo Drive FolderId: mỗi thư mục chỉ được tạo khi CHƯA có trong bản đồ hiện tại
    /// (CreateMappedChildFolderAsync tự kiểm tồn tại trước khi tạo) — lỡ lỗi giữa chừng, lần chạy
    /// lại đọc map mới nhất và chỉ tiếp tục phần còn thiếu, không tạo trùng phần đã xong.
    /// FullTreeReconciledAt chỉ set ở CUỐI, sau khi toàn bộ các bước trên hoàn tất không lỗi.
    /// </summary>
    private async Task ReconcileFullTreeOnceAsync(
        string rootFolderId, Guid dedicatedFolderId, CancellationToken ct)
    {
        var state = await repository.GetSyncStateAsync(ct);
        if (state.FullTreeReconciledAt.HasValue) return;
        if (string.IsNullOrWhiteSpace(rootFolderId)) return;

        var tree = await client.ListFolderTreeAsync(rootFolderId, ct);
        var map = await repository.GetKnownFolderMapAsync(ct);

        // Hội tụ đa-pass giống ReconcileFoldersFromChangesAsync — cây từ ListFolderTreeAsync đã
        // duyệt BFS cha-trước-con, nhưng lặp lại cho chắc (idempotent nên lặp thừa vô hại).
        bool progressed;
        do
        {
            progressed = false;
            foreach (var folder in tree.Folders)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(folder.FolderId)) continue;
                if (map.ContainsKey(folder.FolderId)) continue;

                var parentId = folder.ParentIds.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
                if (parentId is null || !map.TryGetValue(parentId, out var parentKnown)) continue;

                var created = await repository.CreateMappedChildFolderAsync(
                    folder.FolderId, folder.Name, parentId, parentKnown.MediaFolderId, ct);
                map[created.FolderId] = created;
                progressed = true;
            }
        } while (progressed);

        // Đặt lại FolderId cho asset GoogleDrive từng nằm phẳng ở dedicated root.
        foreach (var file in tree.Files)
        {
            ct.ThrowIfCancellationRequested();
            var parentId = file.Parents.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            if (parentId is null || !map.TryGetValue(parentId, out var known)) continue;
            if (known.MediaFolderId == dedicatedFolderId) continue; // đã đúng vị trí gốc.

            var asset = await mediaAssets.FindByGoogleDriveFileIdAsync(file.FileId, ct);
            if (asset is null) continue; // chưa từng import — đường import thường sẽ xử lý sau.
            if (asset.FolderId == known.MediaFolderId) continue;

            await mediaAssets.UpdateGoogleDrivePlacementAsync(asset, known.MediaFolderId, file.Name, ct);
        }

        await repository.MarkFullTreeReconciledAsync(ct);
    }

    private async Task<int> RetryFailuresAsync(
        GoogleDriveOptions settings, Guid dedicatedFolderId, CancellationToken ct)
    {
        var failures = await repository.GetRetryableFailuresAsync(
            settings.MaxRetryAttempts, settings.MaxFilesPerTick, ct);
        var importedCount = 0;

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
                importedCount++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Thử lại file Google Drive {FileId} vẫn lỗi", failure.GoogleDriveFileId);
                await repository.UpsertFailureAsync(
                    failure.GoogleDriveFileId, failure.FileName, failure.MimeType, failure.SizeBytes,
                    ex.Message, ct);
            }
        }

        return importedCount;
    }

    private async Task<int> ImportNewFilesAsync(
        GoogleDriveOptions settings, GoogleDriveSyncStateModel state, Guid dedicatedFolderId, CancellationToken ct)
    {
        var fileStorage = fileStorageOptions.Value;
        var pageToken = state.PageToken;
        var importedExisting = 0;

        // changes.list với startPageToken mới không chứa file đã có trước con trỏ. Snapshot chỉ chạy
        // một lần, kể cả bản cài đã có PageToken (lầ poll đầu cũ bỏ sót file). Cursor cũ được giữ.
        if (!state.InitialSnapshotCompletedAt.HasValue)
        {
            var existing = await client.ListFolderTreeAsync(settings.FolderId, ct);
            importedExisting = await ImportListedFilesAsync(existing.Files, fileStorage, ct);

            if (string.IsNullOrWhiteSpace(pageToken))
            {
                var startToken = await client.GetStartPageTokenAsync(ct);
                await repository.UpdateSyncStateAsync(startToken, importedExisting, ct);
                await repository.MarkInitialSnapshotCompletedAsync(ct);
                return importedExisting;
            }

            await repository.MarkInitialSnapshotCompletedAsync(ct);
        }

        var page = await client.ListChangesAsync(pageToken, settings.MaxFilesPerTick, ct);

        // GetKnownFolderMapAsync tự nâng cấp dòng KnownFolder legacy (MediaFolderId rỗng)
        // về dedicated root — file trong thư mục con GDRIVE-02 cũ không bị bỏ qua âm thầm.
        var map = await repository.GetKnownFolderMapAsync(ct);
        await ReconcileFoldersFromChangesAsync(page.Folders, map, ct);
        await ProcessRemovedOrTrashedAsync(page.RemovedOrTrashedIds, map, ct);

        var importedCount = await ImportListedFilesAsync(page.Files, fileStorage, ct);
        var totalImported = importedExisting + importedCount;

        await repository.UpdateSyncStateAsync(page.NextPageToken, totalImported, ct);
        return totalImported;
    }

    private async Task<int> ImportListedFilesAsync(
        List<GoogleDriveFileInfo> files,
        FileStorageOptions fileStorage,
        CancellationToken ct)
    {
        var map = await repository.GetKnownFolderMapAsync(ct);
        var importedCount = 0;

        foreach (var file in files)
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

                // (c) di chuyển — cha mới đổi so với lần đồng bộ trước.
                if (driveParentId is not null
                    && !string.Equals(known.DriveParentId, driveParentId, StringComparison.Ordinal))
                {
                    if (map.TryGetValue(driveParentId, out var newParent))
                    {
                        // Trong cây — reparent bình thường (hành vi Task A, không đổi).
                        await repository.ReparentMappedMediaFolderAsync(
                            known.MediaFolderId, newParent.MediaFolderId, ct);
                        await repository.UpdateFolderMappingAsync(
                            known.FolderId, driveParentId, known.Name, ct);
                        known.DriveParentId = driveParentId;
                        progressed = true;
                    }
                    else
                    {
                        // GDRIVE-05 Task B: cha mới KHÔNG còn trong bản đồ — di chuyển RA NGOÀI
                        // cây đang theo dõi, coi như bị xoá: cascade soft-delete toàn nhánh.
                        await mediaFolders.CascadeSoftDeleteAsync(known.MediaFolderId, ct);
                        var deletedFolderIds = await repository.CascadeSoftDeleteFolderMappingAsync(
                            known.FolderId, ct);
                        foreach (var deletedId in deletedFolderIds)
                            map.Remove(deletedId);
                        progressed = true;
                    }
                }
            }
        } while (progressed);
    }

    /// <summary>
    /// GDRIVE-05 Task B: xử lý ID file/folder bị xoá hẳn hoặc trash trên Drive. Tra bản đồ thư
    /// mục trước (khớp → cascade soft-delete cả nhánh); không khớp thì tra MediaAssetModel (khớp
    /// → soft-delete đúng 1 file, không cascade). ID chưa từng được biết ở cả hai nơi (file/folder
    /// MỚI mà đã trash sẵn) không khớp gì — giữ nguyên hành vi cũ là bỏ qua im lặng.
    /// </summary>
    private async Task ProcessRemovedOrTrashedAsync(
        List<string> removedOrTrashedIds,
        Dictionary<string, GoogleDriveKnownFolderModel> map,
        CancellationToken ct)
    {
        foreach (var id in removedOrTrashedIds)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(id)) continue;

            if (map.TryGetValue(id, out var known))
            {
                await mediaFolders.CascadeSoftDeleteAsync(known.MediaFolderId, ct);
                var deletedFolderIds = await repository.CascadeSoftDeleteFolderMappingAsync(id, ct);
                foreach (var deletedId in deletedFolderIds)
                    map.Remove(deletedId);
                continue;
            }

            var asset = await mediaAssets.FindByGoogleDriveFileIdAsync(id, ct);
            if (asset is not null)
                await mediaAssets.SoftDeleteAsync(asset.Id, ct);
        }
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
