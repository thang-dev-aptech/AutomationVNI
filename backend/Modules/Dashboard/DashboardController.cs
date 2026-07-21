using Backend.Data;
using Backend.Modules.GenerationJob.Enums;
using Backend.Modules.Post.Enums;
using Backend.Modules.PromptTemplate.Enums;
using Backend.Modules.PublishLog.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Dashboard;

/// <summary>
/// Tổng hợp số liệu dashboard trong 1 request thay vì FE phải fan-out ~16 lần /filter.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IUserContext _userContext;

    public DashboardController(AppDbContext db, IUserContext userContext)
    {
        _db = db;
        _userContext = userContext;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var userId = _userContext.GetCurrentUserId();

        // --- Posts: đếm theo status trong 1 query ---
        var postCounts = await _db.Posts
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var byStatus = postCounts.ToDictionary(x => x.Status, x => x.Count);
        int Count(params PostStatus[] statuses) => statuses.Sum(s => byStatus.GetValueOrDefault(s));

        var recentPosts = await _db.Posts
            .Where(x => !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .Take(20)
            .Select(x => new
            {
                x.Id,
                x.Title,
                Status = (int)x.Status,
                x.CreatedAt,
                x.UserId,
                x.BatchId
            })
            .ToListAsync(ct);

        // --- Channels ---
        var channels = await _db.SocialChannels
            .Where(x => !x.IsDeleted)
            .Select(x => new
            {
                x.Id,
                x.PageName,
                Platform = (int)x.Platform,
                x.IsActive,
                x.TokenExpiresAt
            })
            .ToListAsync(ct);
        var expiredChannels = channels
            .Where(x => x.TokenExpiresAt.HasValue && x.TokenExpiresAt.Value < now)
            .OrderBy(x => x.TokenExpiresAt)
            .Take(10)
            .ToList();

        // --- Media ---
        var mediaTotal = await _db.MediaAssets.CountAsync(x => !x.IsDeleted, ct);

        // --- Jobs: đếm theo status trong 1 query ---
        var jobCounts = await _db.GenerationJobs
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var jobByStatus = jobCounts.ToDictionary(x => x.Status, x => x.Count);
        int JobCount(JobStatus s) => jobByStatus.GetValueOrDefault(s);

        // --- Publish logs failed ---
        var publishFailedTotal = await _db.PublishLogs
            .CountAsync(x => !x.IsDeleted && x.Status == PublishStatus.Failed, ct);
        var publishFailedRecent = await _db.PublishLogs
            .Where(x => !x.IsDeleted && x.Status == PublishStatus.Failed)
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .Select(x => new
            {
                x.Id,
                x.PostId,
                x.SocialChannelId,
                x.ErrorCode,
                x.ErrorMessage,
                x.CreatedAt
            })
            .ToListAsync(ct);

        // --- Prompt templates (danh mục) ---
        var templatesTotal = await _db.PromptTemplates
            .CountAsync(x => !x.IsDeleted && x.TemplateType == PromptTemplateType.Category && x.IsActive, ct);

        // --- Page contexts: độ phủ setup so với kênh ---
        var pageContexts = await _db.PageContexts
            .Where(x => !x.IsDeleted)
            .Select(x => new
            {
                x.SocialChannelId,
                Ready = x.DefaultTextTemplateId != null
                        || x.DefaultImageTemplateId != null
                        || (x.PromptTemplateText != null && x.PromptTemplateText != "")
            })
            .ToListAsync(ct);
        var channelIdsWithContext = pageContexts.Select(x => x.SocialChannelId).ToHashSet();
        var activeChannelCount = channels.Count(x => x.IsActive);
        var channelsMissingContext = channels
            .Count(x => x.IsActive && !channelIdsWithContext.Contains(x.Id));

        // --- Bulk batches đang chạy (posts còn trong pipeline có BatchId) ---
        var activeBatches = await _db.Posts
            .Where(x => !x.IsDeleted && x.BatchId != null
                        && (x.Status == PostStatus.Queued
                            || x.Status == PostStatus.Generating
                            || x.Status == PostStatus.GeneratingMedia
                            || x.Status == PostStatus.RenderingTemplate))
            .Select(x => x.BatchId)
            .Distinct()
            .CountAsync(ct);

        var jobsFailed = JobCount(JobStatus.Failed);
        var jobsDeadLetter = JobCount(JobStatus.DeadLetter);

        var myRecentCount = userId.HasValue
            ? recentPosts.Count(x => x.UserId == userId.Value)
            : 0;

        var data = new
        {
            posts = new
            {
                total = byStatus.Values.Sum(),
                draft = Count(PostStatus.Draft),
                inPipeline = Count(PostStatus.Queued, PostStatus.Generating,
                    PostStatus.GeneratingMedia, PostStatus.RenderingTemplate, PostStatus.Publishing),
                ready = Count(PostStatus.Ready),
                waitingReview = Count(PostStatus.WaitingReview),
                approved = Count(PostStatus.Approved),
                scheduled = Count(PostStatus.Scheduled),
                published = Count(PostStatus.Published),
                failed = Count(PostStatus.Failed),
                needAction = Count(PostStatus.NeedMedia, PostStatus.NeedFix),
                recent = recentPosts,
                myRecentCount,
                available = true
            },
            channels = new
            {
                active = activeChannelCount,
                inactive = channels.Count(x => !x.IsActive),
                total = channels.Count,
                expired = expiredChannels,
                expiredCount = channels.Count(x => x.TokenExpiresAt.HasValue && x.TokenExpiresAt.Value < now),
                available = true
            },
            media = new { total = mediaTotal, available = true },
            jobs = new
            {
                pending = JobCount(JobStatus.Pending),
                running = JobCount(JobStatus.Processing),
                failed = jobsFailed,
                deadLetter = jobsDeadLetter,
                failedTotal = jobsFailed + jobsDeadLetter,
                available = true
            },
            publishLogs = new
            {
                failed = publishFailedTotal,
                recentFailed = publishFailedRecent,
                available = true
            },
            templates = new { total = templatesTotal, available = true },
            pageContexts = new
            {
                total = pageContexts.Count,
                ready = pageContexts.Count(x => x.Ready),
                missingChannels = channelsMissingContext,
                available = true
            },
            bulk = new { activeBatches, available = true },
            sections = new
            {
                posts = true,
                channels = true,
                media = true,
                jobs = true,
                publishLogs = true
            },
            partialErrors = Array.Empty<string>()
        };

        return Ok(ApiResponse.Ok((object)data));
    }
}
