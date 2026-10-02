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

    /// <summary>
    /// Quét ngay: thử lại một đợt các file đã hết MaxRetryAttempts. Không đổi chính sách GDRIVE-01 của worker
    /// định kỳ — tick thường vẫn chỉ lấy AttemptCount dưới MaxRetryAttempts.
    /// </summary>
    public Task<GoogleDriveSyncResult> RunScanNowAsync(CancellationToken ct = default)
        => RunLockedAsync(includeExhaustedFailures: true, ct);

    public Task<GoogleDriveSyncResult> RunTickAsync(CancellationToken ct = default)
        => RunLockedAsync(includeExhaustedFailures: false, ct);

    private async Task<GoogleDriveSyncResult> RunLockedAsync(bool includeExhaustedFailures, CancellationToken ct)
    {
        await Lock.WaitAsync(ct);
        try
        {
            return await RunTickCoreAsync(includeExhaustedFailures, ct);
        }
        finally
        {
            Lock.Release();
        }
    }

    private async Task<GoogleDriveSyncResult> RunTickCoreAsync(bool includeExhaustedFailures, CancellationToken ct)
    {
        var settings = options.Value;
        var state = await repository.GetSyncStateAsync(ct);

        if (!state.IsEnabled)
            return new GoogleDriveSyncResult(Enabled: false, Configured: true, ConfigIssue: null, ImportedCount: 0);

        // GDRIVE-04/05: dedicated folder + dòng ánh xạ root Drive → MediaFolder.
        var dedicatedFolderId = await repository.GetOrCreateDedicatedFolderAsync(settings.FolderId, ct);

        // Snapshot lần đầu tự dựng map từ đúng cây vừa liệt kê. Không reconcile riêng trước đó:
        // một lần ListFolderTree lỗi sẽ bị nuốt ở đây, snapshot chạy với map chỉ có root rồi vẫn
        // đánh dấu xong, file trong thư mục con mất vĩnh viễn.
        if (client.IsConfigured() && state.InitialSnapshotCompletedAt.HasValue)
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

        // File đạt MaxRetryAttempts không được worker tự thử lại. Quét ngay là cách thử lại thủ công.
        var retriedCount = await RetryFailuresAsync(settings, dedicatedFolderId, includeExhaustedFailures, ct);

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
        GoogleDriveOptions settings, Guid dedicatedFolderId, bool includeExhausted, CancellationToken ct)
    {
        var budget = Math.Max(1, settings.MaxFilesPerTick);
        var failures = await repository.GetRetryableFailuresAsync(
            settings.MaxRetryAttempts, budget, ct);
        if (includeExhausted && failures.Count < budget)
        {
            var exhausted = await repository.GetExhaustedFailuresAsync(
                settings.MaxRetryAttempts, budget - failures.Count, ct);
            failures.AddRange(exhausted);
        }
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
        string? capturedStartToken = null;
        var snapshotRemaining = false;
        var snapshotBatchLimit = Math.Max(1, settings.MaxFilesPerTick);

        // changes.list với startPageToken mới không chứa file đã có trước con trỏ. Snapshot chỉ chạy
        // một lần, kể cả bản cài đã có PageToken (lần poll đầu cũ bỏ sót file). Cursor cũ được giữ.
        // Mỗi tick chỉ nhập tối đa MaxFilesPerTick file của snapshot rồi trả khóa. Tick sau liệt kê lại
        // và bỏ file đã có — không lưu page token listing. Chi phí: mỗi đợt gọi lại files.list cả cây.
        if (!state.InitialSnapshotCompletedAt.HasValue)
        {
            // Token lấy TRƯỚC lúc liệt kê lần đầu và được lưu ngay. File tạo sau thời điểm này nằm
            // trong changes của các tick sau; replay idempotent theo GoogleDriveFileId.
            if (string.IsNullOrWhiteSpace(pageToken))
            {
                capturedStartToken = await client.GetStartPageTokenAsync(ct);
                await repository.UpdateSyncStateAsync(capturedStartToken, 0, ct);
                pageToken = capturedStartToken;
            }

            GoogleDriveFolderTree tree;
            try
            {
                tree = await client.ListFolderTreeAsync(settings.FolderId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Snapshot Google Drive chưa liệt kê được cây — chưa đánh dấu xong");
                return 0;
            }

            var unmapped = await ApplyFolderTreeAsync(tree, dedicatedFolderId, ct);
            var folderIds = folderIdsInTree(tree);
            if (!string.IsNullOrWhiteSpace(settings.FolderId))
                folderIds.Add(settings.FolderId);
            var pending = new List<GoogleDriveFileInfo>();
            var remainingAfterBatch = 0;
            foreach (var file in tree.Files)
            {
                if (!await NeedsSnapshotImportAsync(file, fileStorage, folderIds, ct))
                    continue;
                if (pending.Count < snapshotBatchLimit)
                    pending.Add(file);
                else
                    remainingAfterBatch++;
            }

            var listed = await ImportListedFilesAsync(pending, fileStorage, ct, folderIds);
            importedExisting = listed.Imported;
            unmapped |= listed.SkippedUnmappedInTree;
            var stillRetryable = false;
            foreach (var file in pending)
            {
                var failure = await repository.FindFailureAsync(file.FileId, ct);
                if (failure is not null && failure.AttemptCount < settings.MaxRetryAttempts)
                    stillRetryable = true;
            }
            snapshotRemaining = unmapped || remainingAfterBatch > 0 || stillRetryable;

            if (snapshotRemaining)
            {
                logger.LogInformation(
                    "Snapshot Google Drive chưa hết cây — nhập {Imported} file, còn đợt sau",
                    importedExisting);
                return importedExisting;
            }

            await repository.MarkInitialSnapshotCompletedAsync(ct);
            await repository.MarkFullTreeReconciledAsync(ct);
            // Delta để tick sau. Cùng tick vừa giữ khóa suốt đợt snapshot.
            return importedExisting;
        }

        var page = await client.ListChangesAsync(pageToken, settings.MaxFilesPerTick, ct);

        // GetKnownFolderMapAsync tự nâng cấp dòng KnownFolder legacy (MediaFolderId rỗng)
        // về dedicated root — file trong thư mục con GDRIVE-02 cũ không bị bỏ qua âm thầm.
        var map = await repository.GetKnownFolderMapAsync(ct);
        await ReconcileFoldersFromChangesAsync(page.Folders, map, ct);
        await ProcessRemovedOrTrashedAsync(page.RemovedOrTrashedIds, map, ct);

        var importedCount = (await ImportListedFilesAsync(page.Files, fileStorage, ct)).Imported;
        await repository.UpdateSyncStateAsync(page.NextPageToken, importedCount, ct);
        return importedCount;
    }

    private static HashSet<string> folderIdsInTree(GoogleDriveFolderTree tree)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in tree.Folders)
        {
            if (!string.IsNullOrWhiteSpace(folder.FolderId))
                ids.Add(folder.FolderId);
        }
        return ids;
    }

    /// <summary>
    /// File còn phải xử lý trong snapshot: chưa có asset sống, parent đã thuộc cây, và chưa hết lượt thử.
    /// Asset xoá mềm, file ngoài cây, đuôi không cho phép và failure đã đạt MaxRetryAttempts thì bỏ qua.
    /// </summary>
    private async Task<bool> NeedsSnapshotImportAsync(
        GoogleDriveFileInfo file,
        FileStorageOptions fileStorage,
        IReadOnlySet<string> folderIds,
        CancellationToken ct)
    {
        if (file.Parents.Count == 0 || !file.Parents.Any(folderIds.Contains))
            return false;

        var existing = await mediaAssets.FindByGoogleDriveFileIdIncludingDeletedAsync(file.FileId, ct);
        if (existing is not null)
            return false;

        var extension = Path.GetExtension(file.Name);
        if (string.Equals(extension, ZipExtension, StringComparison.OrdinalIgnoreCase)
            || !fileStorage.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (file.SizeBytes > fileStorage.MaxUploadBytes)
            return true;

        var failure = await repository.FindFailureAsync(file.FileId, ct);
        return failure is null || failure.AttemptCount < options.Value.MaxRetryAttempts;
    }

    private sealed record ListedImport(int Imported, bool SkippedUnmappedInTree);

    /// <summary>
    /// Dựng MediaFolder + bản đồ từ đúng cây vừa liệt kê, rồi đặt lại FolderId cho asset đã import
    /// nằm ở dedicated root. Không đánh dấu FullTreeReconciledAt — caller quyết định khi nào xong.
    /// </summary>
    private async Task<bool> ApplyFolderTreeAsync(
        GoogleDriveFolderTree tree, Guid dedicatedFolderId, CancellationToken ct)
    {
        var map = await repository.GetKnownFolderMapAsync(ct);
        var folderIdsInTree = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in tree.Folders)
        {
            if (!string.IsNullOrWhiteSpace(folder.FolderId))
                folderIdsInTree.Add(folder.FolderId);
        }

        var unmapped = false;
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
                if (parentId is null || !map.TryGetValue(parentId, out var parentKnown))
                {
                    if (parentId is not null && folderIdsInTree.Contains(parentId))
                        unmapped = true;
                    continue;
                }

                var created = await repository.CreateMappedChildFolderAsync(
                    folder.FolderId, folder.Name, parentId, parentKnown.MediaFolderId, ct);
                map[created.FolderId] = created;
                progressed = true;
            }
        } while (progressed);

        foreach (var file in tree.Files)
        {
            ct.ThrowIfCancellationRequested();
            var parentId = file.Parents.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            if (parentId is null || !map.TryGetValue(parentId, out var known)) continue;
            if (known.MediaFolderId == dedicatedFolderId) continue;

            var asset = await mediaAssets.FindByGoogleDriveFileIdAsync(file.FileId, ct);
            if (asset is null) continue;
            if (asset.FolderId == known.MediaFolderId) continue;

            await mediaAssets.UpdateGoogleDrivePlacementAsync(asset, known.MediaFolderId, file.Name, ct);
        }

        return unmapped;
    }

    private async Task<ListedImport> ImportListedFilesAsync(
        List<GoogleDriveFileInfo> files,
        FileStorageOptions fileStorage,
        CancellationToken ct,
        IReadOnlySet<string>? folderIdsInTree = null)
    {
        var map = await repository.GetKnownFolderMapAsync(ct);
        var importedCount = 0;
        var skippedUnmappedInTree = false;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var targetFolderId = ResolveMappedMediaFolderId(file.Parents, map);
            if (targetFolderId is null)
            {
                if (folderIdsInTree is not null && file.Parents.Any(parent => folderIdsInTree.Contains(parent)))
                    skippedUnmappedInTree = true;
                continue;
            }

            // Tôn trọng xoá mềm của người dùng: unique index tính cả dòng đã xoá, nhập lại sẽ
            // chết cả tick. Không đếm là lỗi.
            var existing = await mediaAssets.FindByGoogleDriveFileIdIncludingDeletedAsync(file.FileId, ct);
            if (existing is not null)
            {
                if (existing.IsDeleted)
                    continue;
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
                // Entity lỗi vẫn được track trên DbContext chung. Không bỏ nó thì failure row
                // (và mọi SaveChanges sau) ném lại, cả tick abort.
                repository.DiscardPendingChanges();
                try
                {
                    await repository.UpsertFailureAsync(
                        file.FileId, file.Name, file.MimeType, file.SizeBytes, ex.Message, ct);
                }
                catch (Exception failureEx) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(failureEx, "Không ghi được failure row cho file Google Drive {FileId}", file.FileId);
                }
            }
        }

        return new ListedImport(importedCount, skippedUnmappedInTree);
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
