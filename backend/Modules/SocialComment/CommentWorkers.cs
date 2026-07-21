using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Modules.SocialComment;

public class CommentWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int HydrationIntervalSeconds { get; set; } = 10;
    public int HydrationBatchSize { get; set; } = 20;
    public int ReconcileIntervalMinutes { get; set; } = 30;
    public int ReconcileMaxPostsPerChannel { get; set; } = 25;
    public int ReconcileCommentPages { get; set; } = 2;
}

/// <summary>Hydrate pending webhook events quickly.</summary>
public class CommentWebhookHydrationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CommentWorkerOptions> options,
    ILogger<CommentWebhookHydrationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("CommentWebhookHydrationWorker disabled");
            return;
        }

        logger.LogInformation("CommentWebhookHydrationWorker started (interval={Interval}s)", settings.HydrationIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<SocialCommentService>();
                var n = await service.ProcessPendingWebhooksAsync(settings.HydrationBatchSize, stoppingToken);
                if (n > 0)
                    logger.LogInformation("Hydrated {Count} webhook event(s)", n);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "CommentWebhookHydrationWorker loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(3, settings.HydrationIntervalSeconds)), stoppingToken);
        }
    }
}

/// <summary>Periodic reconcile — recent posts/comments to cover missed webhooks.</summary>
public class CommentReconcileWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CommentWorkerOptions> options,
    ILogger<CommentReconcileWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("CommentReconcileWorker disabled");
            return;
        }

        logger.LogInformation(
            "CommentReconcileWorker started (interval={Interval}m)",
            settings.ReconcileIntervalMinutes);

        // Delay first run a bit so app finishes boot / migrations.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
                var service = scope.ServiceProvider.GetRequiredService<SocialCommentService>();

                var channels = await db.SocialChannels
                    .Where(x => !x.IsDeleted && x.IsActive
                                && (x.Platform == SocialPlatform.Facebook || x.Platform == SocialPlatform.Threads)
                                && x.AccessToken != null && x.AccessToken != "")
                    .ToListAsync(stoppingToken);

                foreach (var channel in channels)
                {
                    try
                    {
                        await service.SyncChannelAsync(
                            channel,
                            settings.ReconcileMaxPostsPerChannel,
                            settings.ReconcileCommentPages,
                            stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Reconcile failed for channel {ChannelId}", channel.Id);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "CommentReconcileWorker loop error");
            }

            await Task.Delay(
                TimeSpan.FromMinutes(Math.Max(5, settings.ReconcileIntervalMinutes)),
                stoppingToken);
        }
    }
}
