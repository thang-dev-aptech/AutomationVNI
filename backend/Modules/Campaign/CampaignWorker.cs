using Microsoft.Extensions.Options;

namespace Backend.Modules.Campaign;

/// <summary>
/// Worker định kỳ gọi CampaignGenerationService — khuôn ContentCrawlWorker / PostGenerationWorker.
/// </summary>
public class CampaignWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CampaignWorkerOptions> options,
    ILogger<CampaignWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("CampaignWorker bị tắt trong cấu hình");
            return;
        }

        logger.LogInformation(
            "CampaignWorker chạy (chu kỳ={Interval}s)",
            settings.IntervalSeconds);

        try { await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<CampaignGenerationService>();
                var result = await service.GenerateDueAsync(stoppingToken);
                if (result.Created > 0)
                {
                    logger.LogInformation(
                        "CampaignWorker tạo {Created} bài ({Campaigns} chiến dịch, bỏ qua {Skipped} khe)",
                        result.Created, result.CampaignsProcessed, result.SkippedSlots);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "CampaignWorker lỗi vòng lặp");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Max(15, settings.IntervalSeconds)),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }
}
