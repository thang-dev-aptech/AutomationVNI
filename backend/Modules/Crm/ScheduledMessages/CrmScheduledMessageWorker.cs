using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Crm.ScheduledMessages;

/// <summary>
/// Quét tin hẹn giờ tới hạn và gửi qua PageMessageService.SendAsync, tối đa một lần mỗi tin.
/// Bắt mọi exception (không làm sập host).
/// </summary>
public class CrmScheduledMessageWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CrmScheduledMessageWorkerOptions> options,
    ILogger<CrmScheduledMessageWorker> logger) : BackgroundService
{
    private const int BatchLimit = 50;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var recovered = await scope.ServiceProvider
                .GetRequiredService<CrmScheduledMessageService>().RecoverStuckAsync(ct);
            if (recovered > 0)
                logger.LogWarning("CRM scheduled message: {Count} tin kẹt Sending → Failed (không rõ kết quả)", recovered);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            logger.LogError(ex, "CRM scheduled message: lỗi khi khôi phục tin kẹt");
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var n = await RunOnceAsync(ct);
                if (n > 0)
                    logger.LogInformation("CRM scheduled message: đã xử lý {Count} tin", n);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "CRM scheduled message worker lỗi");
            }

            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Một lượt quét. Mỗi tin dùng scope DI riêng; lỗi một tin không chặn tin kế.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        List<Guid> dueIds;
        using (var scope = scopeFactory.CreateScope())
        {
            dueIds = await scope.ServiceProvider
                .GetRequiredService<CrmScheduledMessageService>().ListDueIdsAsync(BatchLimit, ct);
        }

        var processed = 0;
        foreach (var id in dueIds)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<CrmScheduledMessageService>();
                if (await service.ProcessAsync(id, ct)) processed++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "CRM scheduled message: lỗi xử lý tin {Id}", id);
            }
        }

        return processed;
    }
}
