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

        await telegram.SetupBotProfileAsync(
            [
                ("ds", "Tin đang chờ duyệt"),
                ("dang", "Duyệt và đăng một tin"),
                ("bo", "Bỏ một tin"),
                ("page", "Các page đang bật"),
                ("cao", "Cào tin ngay"),
                ("tt", "Tình trạng hệ thống"),
                ("help", "Hướng dẫn đầy đủ"),
            ],
            "Duyệt và đăng tin giáo dục lên fanpage VNI",
            "Tự cào tin giáo dục từ báo, lọc trùng rồi đẩy về đây. "
            + "Bấm một nút là bài được viết riêng cho từng page và đăng thẳng lên Facebook, "
            + "xong bot gửi lại link để kiểm tra. Gõ /ds để bắt đầu.",
            stoppingToken);

        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        if (_chatId == 0) _chatId = await RecallChatIdAsync(stoppingToken);

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
                await telegram.AnswerCallbackAsync(u.CallbackQueryId, null, ct);

                // Nút của ô chọn page sửa tại chỗ tin nhắn đang mở; gửi tin mới sau mỗi lần
                // tick thì chọn 5 page là 5 tin nhắn rác.
                if (u.Text.StartsWith("sel:") || u.Text.StartsWith("tg:")
                    || u.Text.StartsWith("go:") || u.Text.StartsWith("x:"))
                {
                    var (text, keyboard) = await service.HandleSelectionAsync(
                        u.ChatId, u.Text, u.MessageId, ct);
                    if (u.MessageId is int selMid)
                        await telegram.EditMessageAsync(u.ChatId, selMid, text, keyboard, ct);
                    else
                        await telegram.SendKeyboardAsync(u.ChatId, text, keyboard, ct);
                    continue;
                }

                var reply = await service.HandleCallbackAsync(u.ChatId, u.Text, ct);
                if (u.MessageId is int mid)
                    await telegram.EditMessageAsync(u.ChatId, mid, reply, null, ct);
                else
                    await telegram.SendMessageAsync(u.ChatId, reply, null, ct);
                continue;
            }

            if (!u.Text.StartsWith('/')) continue;
            var answer = await service.HandleCommandAsync(u.ChatId, u.Text, ct);
            var sentId = await telegram.SendKeyboardAsync(u.ChatId, answer.Text, answer.Keyboard, ct);

            // Ô chọn mở bằng lệnh gõ tay là tin nhắn MỚI — nhớ id để lát nữa sửa chính nó
            // thành "Đang đăng…" rồi thành kết quả, thay vì đẻ thêm tin.
            if (answer.Keyboard is not null && sentId is not null)
                await service.RememberPickerMessageAsync(u.ChatId, u.Text, sentId.Value, ct);
        }
    }

    /// <summary>
    /// Nhớ lại chat đã nhắn lần trước, đọc từ tin đã gửi trong DB.
    ///
    /// Không có bước này thì mỗi lần khởi động lại backend là bot câm: nó chỉ học chat_id khi
    /// có người nhắn tới, mà Telegram cấm bot nhắn trước. Sếp sẽ tưởng hệ thống ngừng cào,
    /// trong khi thật ra nó vẫn chạy và chỉ không biết báo về đâu. Đây là kiểu hỏng im lặng
    /// đúng nghĩa — không log lỗi, không ai biết.
    /// </summary>
    private async Task<long> RecallChatIdAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<Backend.Data.AppDbContext>();
            // Chọn chat DÙNG NHIỀU NHẤT, không phải chat gần nhất. Đo thật trên DB: có 2 chat
            // từng nhắn bot — một cái 14 tin, một cái đúng 1 tin. Lấy "gần nhất" thì một người
            // lạ nhắn duy nhất một câu là cướp luôn luồng thông báo của cả hệ thống, im lặng.
            var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .ToListAsync(
                    context.Set<CrawledArticleModel>()
                        .Where(x => x.TelegramChatId != null)
                        .GroupBy(x => x.TelegramChatId!.Value)
                        .Select(g => new { Chat = g.Key, Count = g.Count() }), ct);

            var best = rows.OrderByDescending(r => r.Count).FirstOrDefault();
            if (best is null) return 0;

            logger.LogInformation(
                "Nhớ lại chat Telegram {Chat} ({N} tin đã gửi) từ lần chạy trước", best.Chat, best.Count);
            return best.Chat;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Không đọc lại được chat Telegram");
            return 0;
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
