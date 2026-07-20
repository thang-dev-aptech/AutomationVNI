using Backend.Data;
using Backend.Modules.GenerationJob;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Shared.Generation;

/// <summary>
/// Worker nền rút các post Status=Queued (do bulk-create đẩy vào) và sinh nội dung text+image
/// bất đồng bộ, giới hạn concurrency để tôn trọng rate-limit AI.
/// Xong → Approved (cùng flow create-and-generate, bỏ cổng WaitingReview).
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
                logger.LogInformation("PostGenerationWorker generated+approved post {PostId}", postId);
            }
            else
            {
                logger.LogInformation(
                    "PostGenerationWorker generated post {PostId} (status={Status}, skipped auto-approve)",
                    postId, post?.Status);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PostGenerationWorker failed post {PostId}", postId);
            // Safety net: đưa post ra khỏi Queued để không bị lặp vô hạn mỗi tick.
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var post = await db.Set<PostModel>().FirstOrDefaultAsync(p => p.Id == postId, ct);
                if (post is not null && post.Status is PostStatus.Queued or PostStatus.Generating)
                {
                    post.Status = PostStatus.Failed;
                    post.GenerationError = ex.Message;
                    post.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception saveEx)
            {
                logger.LogWarning(saveEx, "PostGenerationWorker could not mark post {PostId} failed", postId);
            }
        }
    }
}
