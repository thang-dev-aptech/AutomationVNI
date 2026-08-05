using Backend.Data;
using Backend.Modules.GenerationJob;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Shared.Generation;

/// <summary>
/// Worker nền rút các post Status=Queued (do bulk-create / bulk-import đẩy vào) và sinh nội dung text+image
/// bất đồng bộ, giới hạn concurrency để tôn trọng rate-limit AI.
/// Xong → Approved; nếu ExtraJson có pendingSchedule thì tự Schedule.
/// Bật/tắt qua config "GenerationWorker".
/// </summary>
public class PostGenerationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<GenerationWorkerOptions> options,
    ILogger<PostGenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("PostGenerationWorker is disabled by configuration");
            return;
        }

        logger.LogInformation(
            "PostGenerationWorker started (interval={Interval}s, batch={Batch}, concurrency={Concurrency})",
            settings.IntervalSeconds, settings.BatchSize, settings.MaxConcurrency);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueuedAsync(settings, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "PostGenerationWorker loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(3, settings.IntervalSeconds)), stoppingToken);
        }
    }

    private async Task ProcessQueuedAsync(GenerationWorkerOptions settings, CancellationToken ct)
    {
        List<Guid> postIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            postIds = await db.Set<PostModel>()
                .Where(p => !p.IsDeleted && p.Status == PostStatus.Queued)
                .OrderBy(p => p.CreatedAt)
                .Take(Math.Max(1, settings.BatchSize))
                .Select(p => p.Id)
                .ToListAsync(ct);
        }

        if (postIds.Count == 0) return;

        logger.LogInformation("PostGenerationWorker picked {Count} queued post(s)", postIds.Count);

        using var sem = new SemaphoreSlim(Math.Max(1, settings.MaxConcurrency));
        var tasks = postIds.Select(async id =>
        {
            await sem.WaitAsync(ct);
            try { await GenerateOneAsync(id, ct); }
            finally { sem.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private async Task GenerateOneAsync(Guid postId, CancellationToken ct)
    {
        // Mỗi post một scope riêng — DbContext không thread-safe khi chạy song song.
        using var scope = scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<GenerationJobPipelineService>();
        var workflow = scope.ServiceProvider.GetRequiredService<PostWorkflowService>();
        try
        {
            await pipeline.GenerateForPostAsync(postId, ct);

            // Same as create-and-generate: skip review gate → Approved, ready to schedule/publish.
            var post = await workflow.GetPostAsync(postId, ct);
            if (post?.Status == PostStatus.WaitingReview)
            {
                await workflow.ApproveAsync(postId, ct);
                
                if (post.ScheduledPublishAt.HasValue)
                {
                    var scheduledUtc = DateTime.SpecifyKind(post.ScheduledPublishAt.Value, DateTimeKind.Utc);
                    await workflow.ScheduleAsync(postId, scheduledUtc, post.ScheduleTimezone, ct);
                    logger.LogInformation("PostGenerationWorker generated+scheduled post {PostId}", postId);
                }
                else
                {
                    logger.LogInformation("PostGenerationWorker generated+approved post {PostId}", postId);
                }
                post = await workflow.GetPostAsync(postId, ct);
            }
            else
            {
                logger.LogInformation(
                    "PostGenerationWorker generated post {PostId} (status={Status}, skipped auto-approve)",
                    postId, post?.Status);
            }

            await TryApplyPendingScheduleAsync(workflow, post, postId, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PostGenerationWorker failed post {PostId}", postId);
            // Safety net: đưa post ra khỏi Queued để không bị lặp vô hạn mỗi tick.
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var failed = await db.Set<PostModel>().FirstOrDefaultAsync(p => p.Id == postId, ct);
                if (failed is not null && failed.Status is PostStatus.Queued or PostStatus.Generating)
                {
                    failed.Status = PostStatus.Failed;
                    failed.GenerationError = ex.Message;
                    failed.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception saveEx)
            {
                logger.LogWarning(saveEx, "PostGenerationWorker could not mark post {PostId} failed", postId);
            }
        }
    }

    private async Task TryApplyPendingScheduleAsync(
        PostWorkflowService workflow,
        PostModel? post,
        Guid postId,
        CancellationToken ct)
    {
        post ??= await workflow.GetPostAsync(postId, ct);
        if (post is null || post.Status != PostStatus.Approved) return;

        var pending = PendingScheduleHelper.TryRead(post.ExtraJson);
        if (pending is null) return;

        if (!PendingScheduleHelper.TryParseLocalToUtc(pending.AtLocal, pending.Timezone, out var utc))
        {
            logger.LogWarning(
                "PostGenerationWorker skip schedule post {PostId}: cannot parse '{AtLocal}'",
                postId, pending.AtLocal);
            return;
        }

        if (utc <= DateTime.UtcNow)
        {
            logger.LogWarning(
                "PostGenerationWorker skip schedule post {PostId}: time {Utc} already passed",
                postId, utc);
            return;
        }

        try
        {
            await workflow.ScheduleAsync(postId, utc, pending.Timezone, ct);
            logger.LogInformation(
                "PostGenerationWorker auto-scheduled post {PostId} at {Utc} ({Tz} {Local})",
                postId, utc, pending.Timezone, pending.AtLocal);
        }
        catch (Exception ex)
        {
            // Không fail generation — để Approved, user lên lịch tay trên batch page.
            logger.LogWarning(ex, "PostGenerationWorker could not auto-schedule post {PostId}", postId);
        }
    }
}
