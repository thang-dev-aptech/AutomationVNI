using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Crm.Reminders;

/// <summary>Quét nhắc tới hạn và sinh thông báo in-app đúng một lần.</summary>
public class CrmReminderWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<CrmReminderWorkerOptions> options,
    ILogger<CrmReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("CrmReminderWorker bị tắt trong cấu hình");
            return;
        }

        logger.LogInformation(
            "CrmReminderWorker chạy (chu kỳ={Interval}s)",
            settings.IntervalSeconds);

        try { await Task.Delay(TimeSpan.FromSeconds(45), ct); }
        catch (OperationCanceledException) { return; }

        var interval = TimeSpan.FromSeconds(Math.Max(1, settings.IntervalSeconds));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var n = await RunOnceAsync(ct);
                if (n > 0)
                    logger.LogInformation("CRM reminder: đã thông báo {Count} nhắc việc", n);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "CRM reminder worker lỗi");
            }

            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Một lượt quét NotifyDue — dùng trong vòng lặp và trong test.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<CrmReminderService>();
        return await svc.NotifyDueAsync(ct);
    }
}
