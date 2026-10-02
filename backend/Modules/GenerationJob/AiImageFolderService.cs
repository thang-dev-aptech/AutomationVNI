using Backend.Data;
using Backend.Modules.MediaFolder;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.GenerationJob;

/// <summary>
/// Get-or-create folder "Ảnh AI" ngay dưới folder gốc của một Page.
/// Không tự tạo folder gốc. Semaphore xử lý MaxConcurrency=2 của worker trên cùng process.
/// </summary>
public class AiImageFolderService(AppDbContext db, ILogger<AiImageFolderService> logger)
{
    public const string FolderName = "Ảnh AI";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public virtual async Task<Guid?> GetOrCreateAiFolderIdAsync(Guid socialChannelId, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var existing = await FindActiveAiFolderIdAsync(socialChannelId, ct);
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

            var created = new MediaFolderModel
            {
                Id = Guid.NewGuid(),
                Name = FolderName,
                ParentFolderId = root.Id,
                SocialChannelId = root.SocialChannelId,
                CreatedAt = DateTime.UtcNow
            };
            db.MediaFolders.Add(created);
            await db.SaveChangesAsync(ct);

            return await FindActiveAiFolderIdAsync(socialChannelId, ct) ?? created.Id;
        }
        finally
        {
            Gate.Release();
        }
    }

    private Task<Guid?> FindActiveAiFolderIdAsync(Guid socialChannelId, CancellationToken ct)
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
