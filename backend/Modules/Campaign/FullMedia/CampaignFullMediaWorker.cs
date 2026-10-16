using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Campaign.FullMedia;

/// <summary>
/// Worker Full Media — khuôn CampaignWorker. Enabled=false mặc định;
/// không mở scope/DB/AI khi tắt.
/// </summary>
public class CampaignFullMediaWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<FullMediaPipelineOptions> options,
    ILogger<CampaignFullMediaWorker> logger) : BackgroundService
{
    private int _consecutiveFailures;
    private DateTimeOffset? _circuitOpenUntil;

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

        // Đưa Scoring/Verifying treo về Pending
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
            // Re-read Enabled mỗi vòng — tắt giữa chừng thì dừng sạch
            if (!options.Value.Enabled)
            {
                logger.LogInformation("CampaignFullMediaWorker đã tắt — dừng");
                return;
            }

            if (_circuitOpenUntil is { } until && DateTimeOffset.UtcNow < until)
            {
                var wait = until - DateTimeOffset.UtcNow;
                try { await Task.Delay(wait, stoppingToken); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<FullMediaPipelineService>();
                await pipeline.ProcessDueAsync(stoppingToken);
                _consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                logger.LogError(ex, "CampaignFullMediaWorker lỗi vòng lặp (liên tiếp={N})",
                    _consecutiveFailures);
                var threshold = Math.Max(1, options.Value.CircuitBreakerFailures);
                if (_consecutiveFailures >= threshold)
                {
                    var cool = Math.Max(1, options.Value.CircuitBreakerCooldownSeconds);
                    _circuitOpenUntil = DateTimeOffset.UtcNow.AddSeconds(cool);
                    logger.LogWarning(
                        "CampaignFullMediaWorker ngắt mạch {Seconds}s sau {N} lỗi",
                        cool, _consecutiveFailures);
                    _consecutiveFailures = 0;
                }
            }

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
