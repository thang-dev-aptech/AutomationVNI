using Backend.Data;
using Backend.Modules.MediaFolder;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.GenerationJob;

/// <summary>
/// Get-or-create folder "Ảnh AI" ngay dưới folder gốc của một Page.
/// Dùng DbContext riêng để SaveChanges không ghi các thay đổi đang pending của caller.
/// Unique index là ranh giới thật giữa các process; lock trong process chỉ giảm va chạm.
/// </summary>
public class AiImageFolderService(IServiceScopeFactory scopes, ILogger<AiImageFolderService> logger)
{
    public const string FolderName = "Ảnh AI";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public virtual async Task<Guid?> GetOrCreateAiFolderIdAsync(Guid socialChannelId, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await Gate.WaitAsync(ct);
        try
        {
            var existing = await FindActiveAiFolderIdAsync(db, socialChannelId, ct);
            if (existing is not null) return existing;

            var root = await db.MediaFolders.AsNoTracking()
                .Where(x => !x.IsDeleted
                    && x.SocialChannelId == socialChannelId
                    && x.ParentFolderId == null)
                .Select(x => new { x.Id, x.SocialChannelId })
                .FirstOrDefaultAsync(ct);

            if (root is null)
            {
                logger.LogWarning(
                    "Page {SocialChannelId} chưa có folder gốc, không tạo folder Ảnh AI",
                    socialChannelId);
                return null;
            }

            db.MediaFolders.Add(new MediaFolderModel
            {
                Id = Guid.NewGuid(),
                Name = FolderName,
                ParentFolderId = root.Id,
                SocialChannelId = root.SocialChannelId,
                CreatedAt = DateTime.UtcNow
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                logger.LogInformation(ex, "Folder Ảnh AI của Page {SocialChannelId} đã được tạo đồng thời", socialChannelId);
            }

            return await FindActiveAiFolderIdAsync(db, socialChannelId, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static Task<Guid?> FindActiveAiFolderIdAsync(AppDbContext db, Guid socialChannelId, CancellationToken ct)
        => db.MediaFolders.AsNoTracking()
            .Where(x => !x.IsDeleted
                && x.SocialChannelId == socialChannelId
                && x.ParentFolderId != null
                && x.Name == FolderName
                && db.MediaFolders.Any(root =>
                    !root.IsDeleted
                    && root.Id == x.ParentFolderId
                    && root.SocialChannelId == socialChannelId
                    && root.ParentFolderId == null))
            .OrderBy(x => x.CreatedAt)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);
}
