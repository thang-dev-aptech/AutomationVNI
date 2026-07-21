using Microsoft.Extensions.Options;

namespace Backend.Modules.PageMessage;

public class MessageWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int ReconcileIntervalMinutes { get; set; } = 30;
    public int RecentConversationLimit { get; set; } = 100;
}

/// <summary>Đối soát định kỳ để bù các Messenger webhook bị lỡ.</summary>
public class PageMessageReconcileWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MessageWorkerOptions> options,
    ILogger<PageMessageReconcileWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("PageMessageReconcileWorker disabled");
            return;
        }

        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<PageMessageService>();
                var result = await service.SyncAsync(new SyncPageMessagesRequest { Full = false }, stoppingToken);
                if (result.ConversationsUpserted > 0)
                {
                    logger.LogInformation(
                        "Messenger reconcile: {Conversations} conversations, {Messages} messages, {Errors} errors",
                        result.ConversationsUpserted,
                        result.MessagesUpserted,
                        result.Errors.Count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "PageMessageReconcileWorker loop error");
            }

            await Task.Delay(
                TimeSpan.FromMinutes(Math.Max(5, settings.ReconcileIntervalMinutes)),
                stoppingToken);
        }
    }
}
