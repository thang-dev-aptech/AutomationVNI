using Backend.Data;
using Backend.Modules.MediaAsset;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Modules.MediaCaption;

public class MediaCaptionWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 5;
}

public class MediaCaptionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MediaCaptionWorkerOptions> options,
    ILogger<MediaCaptionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        await RecoverRunningAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessOneAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "MediaCaptionWorker loop error"); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds)), stoppingToken);
        }
    }

    protected virtual async Task RecoverRunningAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var items = await db.MediaCaptionJobItems.Where(x => !x.IsDeleted && x.Status == MediaCaptionJobItemStatus.Running).ToListAsync(ct);
        foreach (var item in items) { item.Status = MediaCaptionJobItemStatus.Pending; item.StartedAt = null; }
        if (items.Count > 0) await db.SaveChangesAsync(ct);
    }

    protected virtual async Task ProcessOneAsync(CancellationToken ct)
    {
        Guid? id;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            id = await (from item in db.MediaCaptionJobItems
                        join job in db.MediaCaptionJobs on item.JobId equals job.Id
                        where !item.IsDeleted && !job.IsDeleted && item.Status == MediaCaptionJobItemStatus.Pending
                        orderby job.CreatedAt, item.CreatedAt
                        select item.Id).FirstOrDefaultAsync(ct);
        }
        if (!id.HasValue || id.Value == Guid.Empty) return;
        await ProcessItemAsync(id.Value, ct);
    }

    protected virtual async Task ProcessItemAsync(Guid itemId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<MediaCaptionJobService>();
        var intelligence = scope.ServiceProvider.GetRequiredService<MediaIntelligenceService>();
        var item = await db.MediaCaptionJobItems.FirstOrDefaultAsync(x => x.Id == itemId && !x.IsDeleted, ct);
        if (item is null || item.Status != MediaCaptionJobItemStatus.Pending) return;
        var job = await db.MediaCaptionJobs.SingleAsync(x => x.Id == item.JobId && !x.IsDeleted, ct);
        item.Status = MediaCaptionJobItemStatus.Running;
        item.StartedAt = DateTime.UtcNow;
        item.Attempts++;
        job.Status = MediaCaptionJobStatus.Running;
        await db.SaveChangesAsync(ct);
        try
        {
            var asset = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == item.MediaAssetId && !x.IsDeleted, ct);
            if (asset is null || !string.IsNullOrWhiteSpace(asset.Caption))
            {
                item.Status = MediaCaptionJobItemStatus.Skipped;
                item.FinishedAt = DateTime.UtcNow;
            }
            else
            {
                // Người dùng đang sinh tay ảnh này → chờ xong; GenerateCaptionAsync đọc lại caption và
                // bỏ qua (không gọi AI) nếu đã có.
                await CaptionInFlight.WaitAsync(item.MediaAssetId, InFlightWait, ct);
                // Ghi có điều kiện: người dùng có thể đã ghi caption trong lúc AI chạy → Skipped, không ghi đè.
                var written = await GenerateCaptionAsync(intelligence, item.MediaAssetId, ct);
                item.Status = written ? MediaCaptionJobItemStatus.Succeeded : MediaCaptionJobItemStatus.Skipped;
                item.FinishedAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            item.Status = MediaCaptionJobItemStatus.Failed;
            item.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            item.FinishedAt = DateTime.UtcNow;
            logger.LogWarning(ex, "Media caption item {ItemId} failed", itemId);
        }
        await db.SaveChangesAsync(ct);
        await service.RecalculateAsync(job, ct);
        var active = await db.MediaCaptionJobItems.AnyAsync(x => x.JobId == job.Id && !x.IsDeleted &&
            (x.Status == MediaCaptionJobItemStatus.Pending || x.Status == MediaCaptionJobItemStatus.Running), ct);
        if (!active) { job.Status = MediaCaptionJobStatus.Completed; job.FinishedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
    }

    protected virtual TimeSpan InFlightWait => MediaIntelligenceService.CaptionMaxDuration;

    protected virtual Task<bool> GenerateCaptionAsync(
        MediaIntelligenceService intelligence,
        Guid mediaAssetId,
        CancellationToken ct) => intelligence.GenerateCaptionIfEmptyAsync(mediaAssetId, ct);
}
