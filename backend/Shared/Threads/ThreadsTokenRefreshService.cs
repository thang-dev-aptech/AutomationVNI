using Backend.Data;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Shared.Threads;

/// <summary>
/// Keeps Threads long-lived tokens alive. They expire 60 days after issue and, once expired,
/// cannot be refreshed at all — the user must reconnect. Threads also refuses to refresh a token
/// younger than 24 hours, so freshly connected channels are skipped until they age in.
/// </summary>
public class ThreadsTokenRefreshService(
    IServiceScopeFactory scopeFactory,
    IOptions<ThreadsOAuthOptions> options,
    ILogger<ThreadsTokenRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan MinimumTokenAge = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.RefreshEnabled)
        {
            logger.LogInformation("ThreadsTokenRefreshService is disabled by configuration");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, settings.RefreshIntervalHours));
        logger.LogInformation(
            "ThreadsTokenRefreshService started (interval={IntervalHours}h, refreshBeforeExpiry={Days}d)",
            interval.TotalHours, settings.RefreshBeforeExpiryDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshDueTokensAsync(settings, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ThreadsTokenRefreshService loop error");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RefreshDueTokensAsync(ThreadsOAuthOptions settings, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var oauth = scope.ServiceProvider.GetRequiredService<IThreadsOAuthService>();

        if (!oauth.IsConfigured())
            return;

        var now = DateTime.UtcNow;
        var threshold = now.AddDays(Math.Max(1, settings.RefreshBeforeExpiryDays));

        var due = await db.Set<SocialChannelModel>()
            .Where(x => !x.IsDeleted
                && x.IsActive
                && x.Platform == SocialPlatform.Threads
                && x.TokenExpiresAt != null
                && x.TokenExpiresAt <= threshold
                && x.AccessToken != "")
            .ToListAsync(ct);

        if (due.Count == 0)
            return;

        var refreshed = 0;
        var expired = 0;
        var skipped = 0;

        foreach (var channel in due)
        {
            if (ct.IsCancellationRequested) break;

            // Already dead — refreshing is impossible, surface it instead of retrying every sweep.
            if (channel.TokenExpiresAt <= now)
            {
                logger.LogWarning(
                    "Threads token for channel {ChannelId} ({PageName}) expired at {ExpiredAt:o} — user must reconnect",
                    channel.Id, channel.PageName, channel.TokenExpiresAt);
                expired++;
                continue;
            }

            // Threads rejects refresh on tokens younger than 24h.
            var issuedAt = channel.UpdatedAt ?? channel.CreatedAt;
            if (now - issuedAt < MinimumTokenAge)
            {
                skipped++;
                continue;
            }

            var result = await oauth.RefreshLongLivedTokenAsync(channel.AccessToken, ct);
            if (result is null)
            {
                logger.LogWarning(
                    "Threads token refresh rejected for channel {ChannelId} ({PageName}); will retry next sweep",
                    channel.Id, channel.PageName);
                continue;
            }

            channel.AccessToken = result.Value.Token;
            channel.TokenExpiresAt = result.Value.ExpiresAt;
            channel.UpdatedAt = now;
            channel.UpdatedBy = "threads-token-refresh";
            refreshed++;
        }

        if (refreshed > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Threads token sweep: {Due} due, {Refreshed} refreshed, {Expired} expired, {Skipped} too new",
            due.Count, refreshed, expired, skipped);
    }
}
