using Backend.Shared.Notification;
using Microsoft.Extensions.Options;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Bot Telegram: nhận lệnh bằng long-polling và đẩy thông báo tin mới.
///
/// Vì sao long-polling mà không webhook: webhook bắt Telegram gọi ngược vào máy mình qua
/// HTTPS công khai — localhost không có, VPS thì phải mở cổng và cài chứng chỉ. Long-polling
/// chỉ cần gọi ra ngoài, chạy được ngay trên máy dev và bê lên VPS không đổi gì.
///
/// Một vòng lặp lo cả hai việc: getUpdates chặn tối đa PollTimeoutSeconds rồi trả về, sau đó
/// mới quét tin mới để báo. Nhịp báo tin do đó bằng nhịp polling — đủ nhanh, và tránh phải
/// đẻ thêm một BackgroundService thứ bảy.
/// </summary>
public class CrawlTelegramWorker(
    IServiceScopeFactory scopeFactory,
    TelegramClient telegram,
    IOptions<TelegramOptions> options,
    ILogger<CrawlTelegramWorker> logger) : BackgroundService
{
    /// <summary>
    /// Cursor của Telegram. Update chỉ được xoá khỏi hàng đợi khi lượt getUpdates SAU gửi
    /// offset lớn hơn — nên phải cộng 1 và phải cập nhật kể cả với update bỏ qua, nếu không
    /// Telegram trả lại đúng update đó vô hạn và bot lặp mãi một lệnh.
    /// </summary>
    private long _offset;

    private long _chatId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opt = options.Value;
        if (!opt.Enabled || string.IsNullOrWhiteSpace(opt.BotToken))
        {
            logger.LogInformation("Bot Telegram bị tắt hoặc chưa có token");
            return;
        }

        if (long.TryParse(opt.ChatId, out var configured)) _chatId = configured;
        logger.LogInformation(
            "Bot Telegram chạy (chat mặc định={Chat}, {N} chat được phép ra lệnh)",
            _chatId == 0 ? "chưa biết — chờ tin nhắn đầu tiên" : _chatId.ToString(),
            opt.AllowedChatIds.Count);

        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PumpUpdatesAsync(stoppingToken);
                await PushNotificationsAsync(stoppingToken);
                await PublishApprovedAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Bot Telegram lỗi vòng lặp");
                try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task PumpUpdatesAsync(CancellationToken ct)
    {
        var updates = await telegram.GetUpdatesAsync(_offset, ct);
        foreach (var u in updates)
        {
            _offset = u.UpdateId + 1;
            if (u.ChatId == 0) continue;

            // Chat đầu tiên nhắn tới trở thành nơi nhận thông báo, khỏi phải đi tra chat_id
            // bằng tay. Chỉ học một lần và không ghi xuống đâu cả — restart là học lại.
            if (_chatId == 0) _chatId = u.ChatId;

            if (!IsAllowed(u.ChatId))
            {
                logger.LogWarning("Bỏ qua lệnh từ chat lạ {ChatId}", u.ChatId);
                if (u.CallbackQueryId is not null)
                    await telegram.AnswerCallbackAsync(u.CallbackQueryId, "Không có quyền", ct);
                continue;
            }

            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<CrawlTelegramService>();

            if (u.CallbackQueryId is not null)
            {
                // Trả lời callback TRƯỚC khi làm việc nặng: duyệt một tin có thể mất vài giây,
                // để lâu thì nút cứ quay trên máy người dùng rồi Telegram báo timeout.
                await telegram.AnswerCallbackAsync(u.CallbackQueryId, "Đang xử lý…", ct);
                var reply = await service.HandleCallbackAsync(u.ChatId, u.Text, ct);
                if (u.MessageId is int mid)
                    await telegram.EditMessageAsync(u.ChatId, mid, reply, ct);
                else
                    await telegram.SendMessageAsync(u.ChatId, reply, null, ct);
                continue;
            }

            if (!u.Text.StartsWith('/')) continue;
            var answer = await service.HandleCommandAsync(u.ChatId, u.Text, ct);
            await telegram.SendMessageAsync(u.ChatId, answer, null, ct);
        }
    }

    private async Task PushNotificationsAsync(CancellationToken ct)
    {
        if (_chatId == 0) return;

        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CrawlTelegramService>();
        var sent = await service.NotifyPendingAsync(_chatId, ct);
        if (sent > 0) logger.LogInformation("Đã báo {N} tin mới qua Telegram", sent);
    }

    /// <summary>Đưa tin đã duyệt từ Telegram đi đăng rồi báo link về.</summary>
    private async Task PublishApprovedAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<CrawlAutoPublishService>();
        var done = await service.TickAsync(ct);
        if (done > 0) logger.LogInformation("Đã đăng xong và báo kết quả {N} tin", done);
    }

    /// <summary>Rỗng = cho tất cả (chỉ hợp lúc dev). Có danh sách thì chặn hết phần còn lại.</summary>
    private bool IsAllowed(long chatId)
    {
        var allowed = options.Value.AllowedChatIds;
        return allowed.Count == 0 || allowed.Contains(chatId.ToString());
    }
}
