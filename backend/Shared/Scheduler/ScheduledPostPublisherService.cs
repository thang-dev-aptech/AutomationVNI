using Backend.Modules.PublishLog;
using Backend.Shared;
using Microsoft.Extensions.Options;

namespace Backend.Shared.Scheduler;

public class ScheduledPostPublisherService(
    IServiceScopeFactory scopeFactory,
    IOptions<SchedulerOptions> options,
    ILogger<ScheduledPostPublisherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation("ScheduledPostPublisherService is disabled by configuration");
            return;
        }

        logger.LogInformation(
            "ScheduledPostPublisherService started (interval={IntervalSeconds}s, batch={BatchSize})",
            settings.IntervalSeconds, settings.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueAsync(settings.BatchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ScheduledPostPublisherService loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, settings.IntervalSeconds)), stoppingToken);
        }
    }

    private async Task ProcessDueAsync(int batchSize, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IPublishPipelineService>();
        await pipeline.ProcessDueScheduledAsync(batchSize, ct);
    }
}
