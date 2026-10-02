using Backend.Data;
using Backend.Modules.MediaAsset.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.GenerationJob;

/// <summary>
/// Một lượt khi app khởi động: ảnh AI chưa có folder, gắn đúng một Page còn bài/media chưa xoá,
/// thì chuyển vào folder "Ảnh AI" của Page đó. Chỉ ghi FolderId.
/// </summary>
public class AiImageFolderBackfillService(
    IServiceScopeFactory scopes,
    ILogger<AiImageFolderBackfillService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var folders = scope.ServiceProvider.GetRequiredService<AiImageFolderService>();

            var rows = await (
                from asset in db.MediaAssets
                where !asset.IsDeleted
                    && asset.FolderId == null
                    && asset.Source == MediaSource.AIGenerated
                join link in db.PostMedias on asset.Id equals link.MediaId
                where !link.IsDeleted
                join post in db.Posts on link.PostId equals post.Id
                where !post.IsDeleted
                select new { asset.Id, post.SocialChannelId }
            ).ToListAsync(cancellationToken);

            var moved = 0;
            var skipped = 0;
            foreach (var group in rows.GroupBy(x => x.Id))
            {
                var pages = group.Select(x => x.SocialChannelId).Distinct().ToList();
                if (pages.Count != 1)
                {
                    skipped++;
                    continue;
                }

                var folderId = await folders.GetOrCreateAiFolderIdAsync(pages[0], cancellationToken);
                if (folderId is null)
                {
                    skipped++;
                    continue;
                }

                var updated = await db.MediaAssets
                    .Where(x => x.Id == group.Key
                        && !x.IsDeleted
                        && x.FolderId == null
                        && x.Source == MediaSource.AIGenerated)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(x => x.FolderId, folderId),
                        cancellationToken);
                if (updated == 1) moved++;
                else skipped++;
            }

            logger.LogInformation(
                "Backfill folder Ảnh AI: đã chuyển {Moved} ảnh, bỏ qua {Skipped}",
                moved,
                skipped);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Backfill folder Ảnh AI thất bại, app vẫn khởi động");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
