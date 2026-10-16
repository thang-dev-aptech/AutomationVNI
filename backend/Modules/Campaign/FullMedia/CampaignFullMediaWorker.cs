using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Campaign.FullMedia;

/// <summary>
/// Worker Full Media — khuôn CampaignWorker. Enabled=false mặc định;
/// không mở scope/DB/AI khi tắt. Cầu chì thực thi trong FullMediaPipelineService.
/// </summary>
public class CampaignFullMediaWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<FullMediaPipelineOptions> options,
    TimeProvider timeProvider,
    FullMediaCircuitBreaker circuitBreaker,
    ILogger<CampaignFullMediaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("CampaignFullMediaWorker bị tắt trong cấu hình");
            return;
        }

        logger.LogInformation(
            "CampaignFullMediaWorker chạy (chu kỳ={Interval}s)",
            settings.IntervalSeconds);

        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(Math.Max(0, settings.StartupDelaySeconds)),
                stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            using var bootScope = scopeFactory.CreateScope();
            var pipeline = bootScope.ServiceProvider.GetRequiredService<FullMediaPipelineService>();
            await pipeline.ResetStuckAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CampaignFullMediaWorker reset stuck thất bại");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!options.Value.Enabled)
            {
                logger.LogInformation("CampaignFullMediaWorker đã tắt — dừng");
                return;
            }

            if (circuitBreaker.IsOpen)
            {
                // Chờ tới khi mạch đóng (TimeProvider) — tối đa Interval.
                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(Math.Max(15, options.Value.IntervalSeconds)),
                        stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<FullMediaPipelineService>();
                await pipeline.ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CampaignFullMediaWorker lỗi vòng lặp");
            }

            _ = timeProvider; // giữ inject để test/host resolve
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Max(15, options.Value.IntervalSeconds)),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
