using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaAsset.Enums;
using Backend.Modules.MediaFolder;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.GoogleDrive;

/// <summary>
/// State CRUD cho pipeline nhập file Google Drive: trạng thái đồng bộ (singleton) và hàng đợi
/// retry cho file lỗi. Không chứa logic gọi Drive API hay worker — đó là phần của task khác.
/// </summary>
public class GoogleDriveRepository(AppDbContext context, IUserContext userContext)
    : GenericRepository<GoogleDriveImportFailureModel>(context, userContext)
{
    // ── Trạng thái đồng bộ ──────────────────────────────────────────────────

    public async Task<GoogleDriveSyncStateModel> GetSyncStateAsync(CancellationToken ct = default)
        => await Context.Set<GoogleDriveSyncStateModel>()
            .AsNoTracking()
            .SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId, ct);

    /// <summary>
    /// Advance PageToken và cập nhật thống kê sau mỗi tick — phải gọi kể cả khi
    /// <paramref name="importedCount"/> = 0, để changes.list không lặp lại lô cũ ở lượt sau.
    /// </summary>
    public async Task<GoogleDriveSyncStateModel> UpdateSyncStateAsync(
        string? pageToken, int importedCount, CancellationToken ct = default)
    {
        var state = await Context.Set<GoogleDriveSyncStateModel>()
            .SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId, ct);
        state.PageToken = pageToken;
        state.LastImportedCount = importedCount;
        state.LastSyncAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return state;
    }

    /// <summary>
    /// GDRIVE-05 Task B: đánh dấu ReconcileFullTreeOnceAsync đã chạy xong hoàn toàn — chỉ gọi SAU
    /// KHI toàn bộ cây đã quét/tạo mapping/cập nhật FolderId xong. Chống tạo trùng khi chạy lại
    /// (lỡ lỗi giữa chừng) dựa vào idempotent-theo-FolderId ở CreateMappedChildFolderAsync, không
    /// dựa duy nhất vào cột này.
    /// </summary>
    public async Task MarkFullTreeReconciledAsync(CancellationToken ct = default)
    {
        var state = await Context.Set<GoogleDriveSyncStateModel>()
            .SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId, ct);
        state.FullTreeReconciledAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
    }

    public async Task<GoogleDriveSyncStateModel> SetEnabledAsync(
        bool enabled, string userName, CancellationToken ct = default)
    {
        var state = await Context.Set<GoogleDriveSyncStateModel>()
            .SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId, ct);
        state.IsEnabled = enabled;
        state.UpdatedAt = DateTime.UtcNow;
        state.UpdatedByUserName = string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();
        await Context.SaveChangesAsync(ct);
        return state;
    }

    // ── Hàng đợi retry ──────────────────────────────────────────────────────

    /// <summary>
    /// File còn đáng thử lại (AttemptCount &lt; maxAttempts) — vượt ngưỡng thì worker ngừng
    /// tải lại vĩnh viễn cho file đó nhưng dòng lỗi vẫn ở lại DB để tra cứu thủ công.
    /// </summary>
    public async Task<List<GoogleDriveImportFailureModel>> GetRetryableFailuresAsync(
        int maxAttempts, int batchSize, CancellationToken ct = default)
        => await QueryActive()
            .Where(x => x.AttemptCount < maxAttempts)
            .OrderBy(x => x.LastAttemptAt)
            .Take(Math.Max(1, batchSize))
            .ToListAsync(ct);

    /// <summary>Tạo mới hoặc cộng dồn AttemptCount cho file đã lỗi từ trước, khoá theo GoogleDriveFileId.</summary>
    public async Task<GoogleDriveImportFailureModel> UpsertFailureAsync(
        string googleDriveFileId, string fileName, string mimeType, long sizeBytes,
        string? error, CancellationToken ct = default)
    {
        var existing = await Context.Set<GoogleDriveImportFailureModel>()
            .FirstOrDefaultAsync(x => x.GoogleDriveFileId == googleDriveFileId, ct);

        if (existing is null)
        {
            var entity = new GoogleDriveImportFailureModel
            {
                GoogleDriveFileId = googleDriveFileId,
                FileName = fileName,
                MimeType = mimeType,
                SizeBytes = sizeBytes,
                AttemptCount = 1,
                LastAttemptAt = DateTime.UtcNow,
                LastError = error,
            };
            return await CreateAsync(entity, ct);
        }

        existing.FileName = fileName;
        existing.MimeType = mimeType;
        existing.SizeBytes = sizeBytes;
        existing.AttemptCount += 1;
        existing.LastAttemptAt = DateTime.UtcNow;
        existing.LastError = error;
        ApplyUpdateAudit(existing);
        await Context.SaveChangesAsync(ct);
        return existing;
    }

    /// <summary>Xoá khỏi hàng đợi retry sau khi tải/nhập thành công.</summary>
    public async Task<bool> DeleteFailureAsync(string googleDriveFileId, CancellationToken ct = default)
    {
        var existing = await Context.Set<GoogleDriveImportFailureModel>()
            .FirstOrDefaultAsync(x => x.GoogleDriveFileId == googleDriveFileId, ct);
        if (existing is null) return false;

        Context.Set<GoogleDriveImportFailureModel>().Remove(existing);
        await Context.SaveChangesAsync(ct);
        return true;
    }

    // ── Bản đồ ánh xạ Drive ↔ MediaFolder (GDRIVE-02/05) ─────────────────────

    /// <summary>
    /// Đảm bảo root Drive FolderId có trong bản đồ, gắn MediaFolder dedicated.
    /// Không tạo MediaFolder mới — caller đã GetOrCreateDedicatedFolderAsync.
    /// </summary>
    public async Task EnsureRootFolderKnownAsync(
        string rootFolderId, Guid dedicatedMediaFolderId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootFolderId)) return;
        await CreateFolderMappingAsync(
            rootFolderId.Trim(),
            dedicatedMediaFolderId,
            driveParentId: null,
            name: DedicatedFolderName,
            ct);
    }

    public async Task<HashSet<string>> GetKnownFolderIdsAsync(CancellationToken ct = default)
    {
        var ids = await Context.Set<GoogleDriveKnownFolderModel>()
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Select(x => x.FolderId)
            .ToListAsync(ct);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<Dictionary<string, GoogleDriveKnownFolderModel>> GetKnownFolderMapAsync(
        CancellationToken ct = default)
    {
        // GDRIVE-05 fix: nâng cấp dòng legacy GDRIVE-02 trước khi đọc map — nếu không,
        // file mới trong thư mục con ổn định (không đổi tên/di chuyển) bị bỏ qua âm thầm.
        var dedicatedId = await GetDedicatedFolderIdAsync(ct);
        if (dedicatedId.HasValue)
            await BackfillLegacyKnownFolderMappingsAsync(dedicatedId.Value, ct);

        var rows = await Context.Set<GoogleDriveKnownFolderModel>()
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.MediaFolderId != Guid.Empty)
            .ToListAsync(ct);
        return rows.ToDictionary(x => x.FolderId, StringComparer.Ordinal);
    }

    /// <summary>
    /// Idempotent theo Drive FolderId. Dòng di sản GDRIVE-02 (MediaFolderId rỗng) được
    /// bổ sung MediaFolderId/Name/DriveParentId thay vì no-op.
    /// </summary>
    public async Task CreateFolderMappingAsync(
        string driveFolderId,
        Guid mediaFolderId,
        string? driveParentId,
        string name,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(driveFolderId)) return;
        var id = driveFolderId.Trim();
        var parent = string.IsNullOrWhiteSpace(driveParentId) ? null : driveParentId.Trim();
        var trimmedName = name?.Trim() ?? string.Empty;

        var existing = await Context.Set<GoogleDriveKnownFolderModel>()
            .FirstOrDefaultAsync(x => x.FolderId == id && !x.IsDeleted, ct);
        if (existing is not null)
        {
            // Dòng ánh xạ có thể "lệch" (trỏ MediaFolder đã bị soft-delete/không còn tồn tại)
            // nếu MediaFolder gốc từng bị xoá rồi tạo lại — không chỉ khi còn rỗng từ legacy.
            var needsResync = existing.MediaFolderId == Guid.Empty;
            if (!needsResync && existing.MediaFolderId != mediaFolderId)
            {
                var currentTargetAlive = await Context.Set<MediaFolderModel>()
                    .AnyAsync(f => f.Id == existing.MediaFolderId && !f.IsDeleted, ct);
                needsResync = !currentTargetAlive;
            }

            if (needsResync)
            {
                var staleMediaFolderId = existing.MediaFolderId;
                existing.MediaFolderId = mediaFolderId;
                existing.DriveParentId = parent;
                existing.Name = trimmedName;
                existing.UpdatedAt = DateTime.UtcNow;
                existing.UpdatedBy = UserContext.GetCurrentUserName();
                await Context.SaveChangesAsync(ct);

                // Tự chữa cho dữ liệu đã lỡ tạo dưới MediaFolder cũ (đã xoá) trước khi phát
                // hiện lệch — không để mồ côi vĩnh viễn, cùng tinh thần BackfillOrphan*Async.
                if (staleMediaFolderId != Guid.Empty)
                    await ReparentOrphansOfStaleDedicatedFolderAsync(staleMediaFolderId, mediaFolderId, ct);
            }
            return;
        }

        var entity = new GoogleDriveKnownFolderModel
        {
            Id = Guid.NewGuid(),
            FolderId = id,
            MediaFolderId = mediaFolderId,
            DriveParentId = parent,
            Name = trimmedName,
            DiscoveredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = UserContext.GetCurrentUserName(),
            IsDeleted = false,
        };
        Context.Set<GoogleDriveKnownFolderModel>().Add(entity);
        await Context.SaveChangesAsync(ct);
    }

    public async Task UpdateFolderMappingAsync(
        string driveFolderId,
        string? driveParentId,
        string name,
        CancellationToken ct = default)
    {
        var entity = await Context.Set<GoogleDriveKnownFolderModel>()
            .FirstOrDefaultAsync(x => x.FolderId == driveFolderId && !x.IsDeleted, ct);
        if (entity is null) return;

        entity.DriveParentId = string.IsNullOrWhiteSpace(driveParentId) ? null : driveParentId.Trim();
        entity.Name = name?.Trim() ?? string.Empty;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = UserContext.GetCurrentUserName();
        await Context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Tạo MediaFolder con page-less dưới dedicated tree + dòng ánh xạ. Trả mapping mới.
    /// </summary>
    public async Task<GoogleDriveKnownFolderModel> CreateMappedChildFolderAsync(
        string driveFolderId,
        string name,
        string driveParentId,
        Guid parentMediaFolderId,
        CancellationToken ct = default)
    {
        var folderName = string.IsNullOrWhiteSpace(name) ? driveFolderId : name.Trim();

        var existing = await Context.Set<GoogleDriveKnownFolderModel>()
            .FirstOrDefaultAsync(x => x.FolderId == driveFolderId && !x.IsDeleted, ct);
        if (existing is not null && existing.MediaFolderId != Guid.Empty)
            return existing;

        var mediaFolder = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = folderName,
            SocialChannelId = null,
            ParentFolderId = parentMediaFolderId,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = UserContext.GetCurrentUserName(),
            IsDeleted = false,
        };
        Context.Set<MediaFolderModel>().Add(mediaFolder);

        if (existing is not null)
        {
            existing.MediaFolderId = mediaFolder.Id;
            existing.DriveParentId = driveParentId;
            existing.Name = folderName;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = UserContext.GetCurrentUserName();
            await Context.SaveChangesAsync(ct);
            return existing;
        }

        var mapping = new GoogleDriveKnownFolderModel
        {
            Id = Guid.NewGuid(),
            FolderId = driveFolderId,
            MediaFolderId = mediaFolder.Id,
            DriveParentId = driveParentId,
            Name = folderName,
            DiscoveredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = UserContext.GetCurrentUserName(),
            IsDeleted = false,
        };
        Context.Set<GoogleDriveKnownFolderModel>().Add(mapping);
        await Context.SaveChangesAsync(ct);
        return mapping;
    }

    public async Task RenameMappedMediaFolderAsync(
        Guid mediaFolderId, string newName, CancellationToken ct = default)
    {
        var folder = await Context.Set<MediaFolderModel>()
            .FirstOrDefaultAsync(f => f.Id == mediaFolderId && !f.IsDeleted, ct);
        if (folder is null) return;
        folder.Name = newName.Trim();
        folder.UpdatedAt = DateTime.UtcNow;
        folder.UpdatedBy = UserContext.GetCurrentUserName();
        await Context.SaveChangesAsync(ct);
    }

    public async Task ReparentMappedMediaFolderAsync(
        Guid mediaFolderId, Guid newParentMediaFolderId, CancellationToken ct = default)
    {
        var folder = await Context.Set<MediaFolderModel>()
            .FirstOrDefaultAsync(f => f.Id == mediaFolderId && !f.IsDeleted, ct);
        if (folder is null) return;
        folder.ParentFolderId = newParentMediaFolderId;
        folder.UpdatedAt = DateTime.UtcNow;
        folder.UpdatedBy = UserContext.GetCurrentUserName();
        await Context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// GDRIVE-05 Task B: soft-delete đệ quy TOÀN BỘ dòng ánh xạ trong nhánh bắt đầu từ
    /// <paramref name="rootFolderId"/> (duyệt theo DriveParentId trong bộ nhớ) — dùng song song
    /// với MediaFolderRepository.CascadeSoftDeleteAsync khi một thư mục bị xoá/trash/di chuyển ra
    /// ngoài cây. Trả về danh sách Drive FolderId đã bị loại khỏi bản đồ, để caller gỡ khỏi map
    /// đang dùng trong cùng tick (tránh dùng nhầm mapping vừa bị xoá).
    /// </summary>
    public async Task<List<string>> CascadeSoftDeleteFolderMappingAsync(
        string rootFolderId, CancellationToken ct = default)
    {
        var allActive = await Context.Set<GoogleDriveKnownFolderModel>()
            .Where(x => !x.IsDeleted)
            .ToListAsync(ct);

        var idsToDelete = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(rootFolderId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!idsToDelete.Add(current)) continue;
            foreach (var child in allActive.Where(x =>
                string.Equals(x.DriveParentId, current, StringComparison.Ordinal)))
                stack.Push(child.FolderId);
        }

        var now = DateTime.UtcNow;
        var user = UserContext.GetCurrentUserName();
        var rows = allActive.Where(x => idsToDelete.Contains(x.FolderId)).ToList();
        foreach (var row in rows)
        {
            row.IsDeleted = true;
            row.DeletedAt = now;
            row.DeletedBy = user;
        }

        if (rows.Count > 0)
            await Context.SaveChangesAsync(ct);

        return rows.Select(x => x.FolderId).ToList();
    }

    // ── Folder chuyên dụng (GDRIVE-04) ──────────────────────────────────────

    public const string DedicatedFolderName = "Google Drive";

    /// <summary>
    /// Get-or-create MediaFolder page-less + dòng ánh xạ root Drive FolderId → dedicated.
    /// Lần đầu cũng backfill mọi MediaAsset Source=GoogleDrive còn FolderId=null và nâng cấp
    /// dòng KnownFolder legacy GDRIVE-02 (MediaFolderId rỗng) về dedicated root.
    /// </summary>
    public async Task<Guid> GetOrCreateDedicatedFolderAsync(
        string rootDriveFolderId, CancellationToken ct = default)
    {
        var state = await Context.Set<GoogleDriveSyncStateModel>()
            .SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId, ct);

        Guid dedicatedId;
        if (state.DedicatedFolderId.HasValue)
        {
            var existingId = state.DedicatedFolderId.Value;
            var stillThere = await Context.Set<MediaFolderModel>()
                .AnyAsync(f => f.Id == existingId && !f.IsDeleted, ct);
            if (stillThere)
            {
                dedicatedId = existingId;
                await BackfillOrphanGoogleDriveAssetsAsync(dedicatedId, ct);
                await BackfillLegacyKnownFolderMappingsAsync(dedicatedId, ct);
                await EnsureRootFolderKnownAsync(rootDriveFolderId, dedicatedId, ct);
                await Context.SaveChangesAsync(ct);
                return dedicatedId;
            }
        }

        var folder = new MediaFolderModel
        {
            Id = Guid.NewGuid(),
            Name = DedicatedFolderName,
            SocialChannelId = null,
            ParentFolderId = null,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = UserContext.GetCurrentUserName(),
            IsDeleted = false,
        };
        Context.Set<MediaFolderModel>().Add(folder);

        state.DedicatedFolderId = folder.Id;
        state.UpdatedAt = DateTime.UtcNow;
        dedicatedId = folder.Id;

        await BackfillOrphanGoogleDriveAssetsAsync(dedicatedId, ct);
        await BackfillLegacyKnownFolderMappingsAsync(dedicatedId, ct);
        await Context.SaveChangesAsync(ct);

        await EnsureRootFolderKnownAsync(rootDriveFolderId, dedicatedId, ct);
        return dedicatedId;
    }

    /// <summary>DedicatedFolderId đã lưu trên sync state (null nếu chưa get-or-create).</summary>
    public async Task<Guid?> GetDedicatedFolderIdAsync(CancellationToken ct = default)
    {
        var state = await Context.Set<GoogleDriveSyncStateModel>()
            .AsNoTracking()
            .SingleAsync(x => x.Id == GoogleDriveSyncStateModel.SingletonId, ct);
        return state.DedicatedFolderId;
    }

    private async Task BackfillOrphanGoogleDriveAssetsAsync(Guid dedicatedFolderId, CancellationToken ct)
    {
        var orphans = await Context.Set<MediaAssetModel>()
            .Where(a => !a.IsDeleted
                && a.Source == MediaSource.GoogleDrive
                && a.FolderId == null)
            .ToListAsync(ct);
        foreach (var asset in orphans)
            asset.FolderId = dedicatedFolderId;
    }

    /// <summary>
    /// Fix bug e0cd97fc: khi root mapping phát hiện đang trỏ một MediaFolder cũ đã bị
    /// soft-delete (<paramref name="staleMediaFolderId"/>) và được sửa lại đúng dedicated
    /// folder hiện hành (<paramref name="currentDedicatedFolderId"/>), mọi MediaFolder/MediaAsset
    /// từng lỡ được tạo dưới folder cũ đó (trong lúc mapping còn lệch) phải được chuyển sang
    /// folder hiện hành — nếu không, chúng "mồ côi" vĩnh viễn dưới 1 cha đã xoá, không thể xem
    /// được trên UI dù DB có đủ dữ liệu (đúng triệu chứng đã tái hiện).
    /// </summary>
    private async Task ReparentOrphansOfStaleDedicatedFolderAsync(
        Guid staleMediaFolderId, Guid currentDedicatedFolderId, CancellationToken ct)
    {
        var orphanFolders = await Context.Set<MediaFolderModel>()
            .Where(f => !f.IsDeleted && f.ParentFolderId == staleMediaFolderId)
            .ToListAsync(ct);
        foreach (var folder in orphanFolders)
            folder.ParentFolderId = currentDedicatedFolderId;

        var orphanAssets = await Context.Set<MediaAssetModel>()
            .Where(a => !a.IsDeleted && a.FolderId == staleMediaFolderId)
            .ToListAsync(ct);
        foreach (var asset in orphanAssets)
            asset.FolderId = currentDedicatedFolderId;

        if (orphanFolders.Count > 0 || orphanAssets.Count > 0)
            await Context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// GDRIVE-05: dòng KnownFolder di sản GDRIVE-02 chỉ có FolderId (MediaFolderId=Empty sau
    /// migration). Tạm map về dedicated root — không mất file; ReconcileFullTreeOnce (Task B)
    /// sẽ đặt lại đúng vị trí cây sau.
    /// </summary>
    private async Task BackfillLegacyKnownFolderMappingsAsync(
        Guid dedicatedFolderId, CancellationToken ct)
    {
        var legacy = await Context.Set<GoogleDriveKnownFolderModel>()
            .Where(x => !x.IsDeleted && x.MediaFolderId == Guid.Empty)
            .ToListAsync(ct);
        if (legacy.Count == 0) return;

        var now = DateTime.UtcNow;
        var user = UserContext.GetCurrentUserName();
        foreach (var row in legacy)
        {
            row.MediaFolderId = dedicatedFolderId;
            if (string.IsNullOrWhiteSpace(row.Name))
                row.Name = row.FolderId;
            row.UpdatedAt = now;
            row.UpdatedBy = user;
        }

        await Context.SaveChangesAsync(ct);
    }
}
