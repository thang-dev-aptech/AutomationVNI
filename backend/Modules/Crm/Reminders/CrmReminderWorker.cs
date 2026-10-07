using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backend.Modules.Crm.Reminders;

/// <summary>Quét nhắc tới hạn và sinh thông báo in-app đúng một lần.</summary>
public class CrmReminderWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<CrmReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), ct); }
        catch (OperationCanceledException) { return; }

        var interval = TimeSpan.FromMinutes(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<CrmReminderService>();
                var n = await svc.NotifyDueAsync(ct);
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
}
