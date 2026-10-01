using Backend.Data;
using Backend.Modules.MediaAsset;
using Backend.Modules.MediaFolder;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.MediaCaption;

public class MediaCaptionJobService(AppDbContext db, MediaFolderRepository folders)
{
    public async Task<MediaCaptionJobModel> CreateAsync(Guid folderId, CancellationToken ct = default)
    {
        var folder = await db.MediaFolders.FirstOrDefaultAsync(x => x.Id == folderId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy folder");
        if (!await folders.IsInGoogleDriveTreeAsync(folderId, ct))
            throw new ArgumentException("Folder phải thuộc cây Google Drive");

        var existing = await db.MediaCaptionJobs
            .Where(x => x.FolderId == folderId && !x.IsDeleted &&
                (x.Status == MediaCaptionJobStatus.Queued || x.Status == MediaCaptionJobStatus.Running))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (existing is not null) return existing;

        var folderIds = await folders.GetDescendantFolderIdsAsync(folderId, ct);
        var assets = await db.MediaAssets
            .Where(x => !x.IsDeleted && x.FolderId.HasValue && folderIds.Contains(x.FolderId.Value)
                && x.MimeType.StartsWith("image/"))
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { x.Id, x.FileName, x.OriginalFileName, x.Caption })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var job = new MediaCaptionJobModel
        {
            Id = Guid.NewGuid(), FolderId = folder.Id, FolderName = folder.Name,
            Status = MediaCaptionJobStatus.Queued, CreatedAt = now
        };
        var pending = assets.Where(x => string.IsNullOrWhiteSpace(x.Caption)).ToList();
        job.Skipped = assets.Count - pending.Count;
        job.Total = pending.Count;
        db.MediaCaptionJobs.Add(job);
        foreach (var asset in pending)
        {
            db.MediaCaptionJobItems.Add(new MediaCaptionJobItemModel
            {
                Id = Guid.NewGuid(), JobId = job.Id, MediaAssetId = asset.Id,
                FileName = asset.OriginalFileName ?? asset.FileName,
                Status = MediaCaptionJobItemStatus.Pending, CreatedAt = now
            });
        }
        if (pending.Count == 0)
        {
            job.Status = MediaCaptionJobStatus.Completed;
            job.FinishedAt = now;
        }
        try
        {
            await db.SaveChangesAsync(ct);
            return job;
        }
        catch (DbUpdateException)
        {
            // Partial unique index can be hit by two requests that both observed no active job.
            // Discard the losing insert and return the job created by the concurrent request.
            db.ChangeTracker.Clear();
            var concurrent = await db.MediaCaptionJobs.AsNoTracking()
                .Where(x => x.FolderId == folderId && !x.IsDeleted &&
                    (x.Status == MediaCaptionJobStatus.Queued || x.Status == MediaCaptionJobStatus.Running))
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (concurrent is not null) return concurrent;
            throw;
        }
    }

    public async Task<MediaCaptionJobResponse> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await db.MediaCaptionJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == jobId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy caption job");
        var items = await db.MediaCaptionJobItems.AsNoTracking().Where(x => x.JobId == jobId && !x.IsDeleted)
            .OrderBy(x => x.CreatedAt).ToListAsync(ct);
        return ToResponse(job, items);
    }

    public async Task<List<MediaCaptionJobResponse>> ListRecentAsync(int take = 20, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var jobs = await db.MediaCaptionJobs.AsNoTracking().Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt).Take(take).ToListAsync(ct);
        return jobs.Select(x => ToResponse(x, [])).ToList();
    }

    public async Task RetryItemAsync(Guid itemId, CancellationToken ct = default)
    {
        var item = await db.MediaCaptionJobItems.FirstOrDefaultAsync(x => x.Id == itemId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy caption job item");
        if (item.Status != MediaCaptionJobItemStatus.Failed)
            throw new InvalidOperationException("Chỉ retry được item lỗi");
        item.Status = MediaCaptionJobItemStatus.Pending;
        item.Error = null;
        item.StartedAt = null;
        item.FinishedAt = null;
        var job = await db.MediaCaptionJobs.SingleAsync(x => x.Id == item.JobId, ct);
        job.Status = MediaCaptionJobStatus.Running;
        job.FinishedAt = null;
        await RecalculateAsync(job, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task RetryFailedAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await db.MediaCaptionJobs.FirstOrDefaultAsync(x => x.Id == jobId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Không tìm thấy caption job");
        var failed = await db.MediaCaptionJobItems.Where(x => x.JobId == jobId && !x.IsDeleted && x.Status == MediaCaptionJobItemStatus.Failed)
            .ToListAsync(ct);
        foreach (var item in failed)
        {
            item.Status = MediaCaptionJobItemStatus.Pending;
            item.Error = null;
            item.StartedAt = null;
            item.FinishedAt = null;
        }
        if (failed.Count > 0)
        {
            job.Status = MediaCaptionJobStatus.Running;
            job.FinishedAt = null;
        }
        await RecalculateAsync(job, ct);
        await db.SaveChangesAsync(ct);
    }

    internal async Task RecalculateAsync(MediaCaptionJobModel job, CancellationToken ct = default)
    {
        var statuses = await db.MediaCaptionJobItems.Where(x => x.JobId == job.Id && !x.IsDeleted)
            .GroupBy(x => x.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(ct);
        int Count(MediaCaptionJobItemStatus status) => statuses.FirstOrDefault(x => x.Status == status)?.Count ?? 0;
        // Asset đã có caption lúc tạo job không có item theo contract; giữ baseline đó khi
        // một Pending item bị skip vì người dùng thêm caption trước lúc worker xử lý.
        var priorItemSkipped = Count(MediaCaptionJobItemStatus.Skipped);
        var initialSkipped = Math.Max(0, job.Skipped - priorItemSkipped);
        job.Total = statuses.Sum(x => x.Count);
        job.Succeeded = Count(MediaCaptionJobItemStatus.Succeeded);
        job.Failed = Count(MediaCaptionJobItemStatus.Failed);
        job.Skipped = initialSkipped + priorItemSkipped;
    }

    internal static MediaCaptionJobResponse ToResponse(MediaCaptionJobModel job, IEnumerable<MediaCaptionJobItemModel> items) => new()
    {
        Id = job.Id, FolderId = job.FolderId, FolderName = job.FolderName, Status = job.Status.ToString(),
        Total = job.Total, Succeeded = job.Succeeded, Failed = job.Failed, Skipped = job.Skipped,
        CreatedAt = job.CreatedAt, CreatedBy = job.CreatedBy, FinishedAt = job.FinishedAt,
        Items = items.Select(x => new MediaCaptionJobItemResponse
        {
            Id = x.Id, MediaAssetId = x.MediaAssetId, FileName = x.FileName, Status = x.Status.ToString(),
            Error = x.Error, Attempts = x.Attempts, StartedAt = x.StartedAt, FinishedAt = x.FinishedAt,
            PreviewUrl = MediaAssetUrls.Preview(x.MediaAssetId)
        }).ToList()
    };
}
