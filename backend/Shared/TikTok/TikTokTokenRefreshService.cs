using Backend.Data;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Shared.TikTok;

/// <summary>
/// Giữ access token TikTok sống — chỉ tồn tại ~24h (khác Threads 60 ngày) nên phải quét dày hơn
/// nhiều. Mỗi lần refresh thành công, TikTok rotate CẢ access_token lẫn refresh_token — nếu chỉ
/// cập nhật access token mà bỏ quên refresh_token mới thì lần refresh kế tiếp sẽ dùng refresh_token
/// cũ đã bị TikTok thu hồi và thất bại.
/// </summary>
public class TikTokTokenRefreshService(
    IServiceScopeFactory scopeFactory,
    IOptions<TikTokOAuthOptions> options,
    ILogger<TikTokTokenRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.RefreshEnabled)
        {
            logger.LogInformation("TikTokTokenRefreshService is disabled by configuration");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(15, settings.RefreshIntervalMinutes));
        logger.LogInformation(
            "TikTokTokenRefreshService started (interval={IntervalMinutes}m, refreshBeforeExpiry={Hours}h)",
            interval.TotalMinutes, settings.RefreshBeforeExpiryHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshDueTokensAsync(settings, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "TikTokTokenRefreshService loop error");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RefreshDueTokensAsync(TikTokOAuthOptions settings, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var oauth = scope.ServiceProvider.GetRequiredService<ITikTokOAuthService>();

        if (!oauth.IsConfigured())
            return;

        var now = DateTime.UtcNow;
        var threshold = now.AddHours(Math.Max(1, settings.RefreshBeforeExpiryHours));

        var due = await db.Set<SocialChannelModel>()
            .Where(x => !x.IsDeleted
                && x.IsActive
                && x.Platform == SocialPlatform.TikTok
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

            if (channel.TokenExpiresAt <= now)
            {
                logger.LogWarning(
                    "TikTok access token for channel {ChannelId} ({PageName}) expired at {ExpiredAt:o} — thử refresh, nếu refresh_token cũng hết hạn thì user phải kết nối lại",
                    channel.Id, channel.PageName, channel.TokenExpiresAt);
                expired++;
            }

            if (string.IsNullOrWhiteSpace(channel.RefreshToken))
            {
                logger.LogWarning(
                    "TikTok channel {ChannelId} ({PageName}) không có refresh_token lưu sẵn — bỏ qua, user phải kết nối lại",
                    channel.Id, channel.PageName);
                skipped++;
                continue;
            }

            var result = await oauth.RefreshTokenAsync(channel.RefreshToken, ct);
            if (result is null)
            {
                logger.LogWarning(
                    "TikTok token refresh bị từ chối cho channel {ChannelId} ({PageName}); sẽ thử lại lần quét sau",
                    channel.Id, channel.PageName);
                continue;
            }

            channel.AccessToken = result.AccessToken;
            channel.RefreshToken = result.RefreshToken;
            channel.TokenExpiresAt = result.AccessTokenExpiresAt;
            channel.UpdatedAt = now;
            channel.UpdatedBy = "tiktok-token-refresh";
            refreshed++;
        }

        if (refreshed > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "TikTok token sweep: {Due} due, {Refreshed} refreshed, {Expired} expired, {Skipped} thiếu refresh_token",
            due.Count, refreshed, expired, skipped);
    }
}
