using Backend.Data;
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

    // ── Tập thư mục đã biết (GDRIVE-02) ─────────────────────────────────────

    /// <summary>
    /// Đảm bảo root FolderId luôn có trong tập đã biết từ tick đầu — không backfill cây cũ,
    /// chỉ seed điểm gốc để thư mục con phát hiện sau đó gắn vào cây.
    /// </summary>
    public async Task EnsureRootFolderKnownAsync(string rootFolderId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootFolderId)) return;
        await AddKnownFolderAsync(rootFolderId.Trim(), ct);
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

    /// <summary>Idempotent theo FolderId — gọi lại với id đã biết là no-op.</summary>
    public async Task AddKnownFolderAsync(string folderId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderId)) return;
        var id = folderId.Trim();

        var exists = await Context.Set<GoogleDriveKnownFolderModel>()
            .AnyAsync(x => x.FolderId == id && !x.IsDeleted, ct);
        if (exists) return;

        var entity = new GoogleDriveKnownFolderModel
        {
            Id = Guid.NewGuid(),
            FolderId = id,
            DiscoveredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = UserContext.GetCurrentUserName(),
            IsDeleted = false,
        };
        Context.Set<GoogleDriveKnownFolderModel>().Add(entity);
        await Context.SaveChangesAsync(ct);
    }
}
