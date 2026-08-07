using System.Net;
using System.Text;
using Backend.Data;
using Backend.Modules.ContentCrawl.Enums;
using Backend.Shared.Text;
using Microsoft.EntityFrameworkCore;
using Backend.Shared.Notification;
using Microsoft.Extensions.Options;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Cầu nối giữa bot Telegram và luồng duyệt tin.
///
/// Nguyên tắc thiết kế: Telegram CHỈ lo quyết định "đăng hay bỏ", không lo tinh chỉnh.
/// Mọi tham số của lệnh duyệt (page, template, sinh ảnh, lịch đăng) đều đã có mặc định ở
/// ApproveCrawledArticleRequest, nên /dang không cần tham số nào. Ai muốn đổi thì mở web —
/// nhồi 4 ô cấu hình vào khung chat chỉ tổ gõ nhầm rồi đăng sai page.
/// </summary>
/// <summary>Câu trả lời của bot: chữ, kèm bàn phím nếu có.</summary>
public sealed record BotReply(string Text, List<List<TelegramButton>>? Keyboard = null);

public class CrawlTelegramService(
    AppDbContext context,
    CrawlTelegramSelectionStore selections,
    ContentCrawlRepository repository,
    ContentCrawlPipelineService pipeline,
    Backend.Modules.NewsSite.NewsSiteRepository newsRepository,
    Backend.Modules.NewsSite.NewsFanpageService fanpage,
    Backend.Modules.Notification.NotificationService notifications,
    TelegramClient telegram,
    IOptions<TelegramOptions> options,
    ILogger<CrawlTelegramService> logger)
{
    /// <summary>
    /// Mã hiện cho người dùng gõ. Ưu tiên số thứ tự nhỏ (1, 2, 3…); tin chưa được cấp số thì
    /// rơi về 6 ký tự hex — vẫn tra được, chỉ khó gõ hơn.
    /// </summary>
    public static string ShortId(CrawledArticleModel a)
        => a.ShortCode is int n ? n.ToString() : a.Id.ToString("N")[..6].ToLowerInvariant();

    // ── Báo tin mới ─────────────────────────────────────────────────────────

    /// <summary>
    /// Gửi tin chờ duyệt chưa báo lần nào. Ghi TelegramMessageId ngay sau khi gửi để lần
    /// quét sau không báo lại — đây là toàn bộ cơ chế chống gửi trùng, không có gì khác.
    /// </summary>
    public async Task<int> NotifyPendingAsync(long chatId, CancellationToken ct = default)
    {
        await repository.EnsureShortCodesAsync(ct);
        var articles = await repository.GetUnnotifiedPendingAsync(options.Value.MaxNotifyPerTick, ct);
        if (articles.Count == 0) return 0;

        // Nạp trước tên page mặc định của từng nguồn: tin nhắn phải nói rõ bấm nút là đăng
        // đi ĐÂU. Không có dòng đó thì nút "Duyệt & đăng" là một cú nhảy vào bóng tối.
        var pagesBySource = await ResolveDefaultPageNamesAsync(
            articles.Select(a => a.CrawlSourceId).Distinct().ToList(), ct);

        var sent = 0;
        foreach (var a in articles)
        {
            ct.ThrowIfCancellationRequested();
            var code = ShortId(a);
            var caption = FormatArticle(a, pagesBySource.GetValueOrDefault(a.CrawlSourceId));
            List<List<TelegramButton>> buttons =
            [
                [new TelegramButton("✅ Đưa lên web", $"ok:{code}")],
                [new TelegramButton("📋 Chọn page", $"sel:{code}"),
                 new TelegramButton("🗑 Bỏ", $"no:{code}")],
            ];

            // Ảnh minh hoạ chỉ để NHÌN cho dễ trong lúc duyệt — không bao giờ đăng lại lên
            // fanpage (bản quyền toà soạn), đó cũng là lý do ImportThumbnails mặc định false.
            // CDN chặn hotlink hoặc ảnh quá nặng thì rơi về tin chữ, mất ảnh chứ không mất tin.
            var messageId = string.IsNullOrWhiteSpace(a.ThumbnailUrl)
                ? await telegram.SendKeyboardAsync(chatId, caption, buttons, ct)
                : await telegram.SendPhotoAsync(chatId, a.ThumbnailUrl!, caption, buttons, ct)
                  ?? await telegram.SendKeyboardAsync(chatId, caption, buttons, ct);

            if (messageId is null)
            {
                // Gửi hỏng thì ĐỪNG đánh dấu đã báo — để lượt sau thử lại. Đánh dấu ở đây
                // là mất tin trong im lặng: web vẫn thấy nhưng Telegram không bao giờ nhắc.
                logger.LogWarning("Không gửi được tin {Id} qua Telegram", a.Id);
                continue;
            }

            a.TelegramChatId = chatId;
            a.TelegramMessageId = messageId;
            await repository.UpdateAsync(a, ct);
            sent++;
        }

        return sent;
    }

    /// <summary>Nguồn → tên các page mặc định của nguồn đó.</summary>
    private async Task<Dictionary<Guid, string>> ResolveDefaultPageNamesAsync(
        List<Guid> sourceIds, CancellationToken ct)
    {
        var sources = await context.Set<CrawlSourceModel>()
            .Where(s => sourceIds.Contains(s.Id))
            .Select(s => new { s.Id, s.DefaultChannelIds })
            .ToListAsync(ct);

        var names = await context.SocialChannels
            .Where(c => !c.IsDeleted)
            .ToDictionaryAsync(c => c.Id, c => c.PageName, ct);

        return sources.ToDictionary(
            s => s.Id,
            s => string.Join(", ", ContentCrawlRepository.DeserializeGuidList(s.DefaultChannelIds)
                .Select(id => names.GetValueOrDefault(id))
                .Where(n => !string.IsNullOrWhiteSpace(n))));
    }

    private static string FormatArticle(CrawledArticleModel a, string? defaultPages)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<b>{Esc(a.Title)}</b>");
        sb.AppendLine();
        // Tóm tắt do AI viết đứng ngay dưới tít: đây mới là thứ đọc lướt trên điện thoại là
        // quyết được. Số ký tự của bài gốc thì chẳng nói lên điều gì.
        if (!string.IsNullOrWhiteSpace(a.ScreenSummary))
        {
            sb.AppendLine(Esc(a.ScreenSummary));
            sb.AppendLine();
        }

        var meta = new List<string> { $"<code>#{ShortId(a)}</code>" };
        if (a.QualityScore is int score) meta.Add($"{ScoreIcon(score)} {score} điểm");
        if (!string.IsNullOrWhiteSpace(a.ScreenTopic)) meta.Add(Esc(a.ScreenTopic));
        sb.AppendLine(string.Join(" · ", meta));
        if (!string.IsNullOrWhiteSpace(a.SourceUrl)) sb.AppendLine(Esc(a.SourceUrl));
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(defaultPages)
            ? "Bấm nút = đưa lên web (chưa đăng fanpage)"
            : $"Bấm nút = đưa lên web. Fanpage mặc định sau đó: <b>{Esc(defaultPages)}</b>");
        return sb.ToString().TrimEnd();
    }

    private static string Esc(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string ScoreIcon(int score) => score >= 85 ? "🟢" : score >= 70 ? "🟡" : "🟠";

    // ── Xử lý lệnh ──────────────────────────────────────────────────────────

    /// <summary>Trả về câu trả lời để bot gửi lại. Không bao giờ ném — lỗi thành lời nhắn.</summary>
    public async Task<BotReply> HandleCommandAsync(long chatId, string text, CancellationToken ct = default)
    {
        var parts = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return new BotReply(Help());

        // "/dang@vni_auto_bot" trong group — Telegram tự gắn tên bot vào lệnh.
        var cmd = parts[0].ToLowerInvariant().TrimStart('/').Split('@')[0];
        var arg = parts.Length > 1 ? parts[1].Trim() : "";

        try
        {
            // "/dang <mã>" trần trụi thì MỞ Ô CHỌN PAGE thay vì đăng luôn. Gõ kèm tên page
            // ("/dang a3f9c1 toefl") mới đi thẳng — người đã gõ tên page là người biết mình
            // muốn gì, bắt bấm thêm một màn nữa chỉ tổ chậm.
            if (cmd is "dang" or "duyet" && !arg.Contains(' '))
            {
                var (text2, kb) = await HandleSelectionAsync(chatId, $"sel:{arg.Trim()}", null, ct);
                return new BotReply(text2, kb);
            }

            return cmd switch
            {
                "dang" or "duyet" => new BotReply(await ApproveAsync(chatId, arg, ct)),
                "fb" or "dangfb" => new BotReply(await ToFanpageAsync(arg, ct)),
                "bo" or "loai" => new BotReply(await RejectAsync(arg, ct)),
                "ds" or "list" => new BotReply(await ListPendingAsync(ct)),
                "tt" or "status" => new BotReply(await StatusAsync(ct)),
                "cao" or "crawl" => new BotReply(await CrawlNowAsync(ct)),
                "page" or "pages" => new BotReply(await ListPagesAsync(ct)),
                "start" => new BotReply(await StartAsync(ct)),
                "help" => new BotReply(Help()),
                _ => new BotReply($"Không hiểu lệnh <code>/{Esc(cmd)}</code>.\n\n{Help()}"),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Lệnh Telegram '{Text}' lỗi", text);
            return new BotReply($"⚠️ Lỗi: {Esc(ex.Message)}");
        }
    }

    /// <summary>Nút bấm dưới tin nhắn: <c>ok:abc123</c> / <c>no:abc123</c>.</summary>
    public async Task<string> HandleCallbackAsync(long chatId, string data, CancellationToken ct = default)
    {
        var bits = data.Split(':', 2);
        if (bits.Length != 2) return "Nút không hợp lệ";
        return bits[0] switch
        {
            "ok" => await ApproveAsync(chatId, bits[1], ct),
            "no" => await RejectAsync(bits[1], ct),
            _ => "Nút không hợp lệ",
        };
    }

    // ── Ô chọn page ─────────────────────────────────────────────────────────

    /// <summary>
    /// Callback của ô chọn page. Trả về (nội dung, bàn phím) để worker SỬA TẠI CHỖ tin nhắn
    /// đang mở — gửi tin mới sau mỗi lần tick thì 5 lần tick là 5 tin nhắn rác.
    /// Bàn phím null nghĩa là gỡ hết nút.
    /// </summary>
    public async Task<(string Text, List<List<TelegramButton>>? Keyboard)> HandleSelectionAsync(
        long chatId, string data, int? messageId, CancellationToken ct = default)
    {
        var bits = data.Split(':');
        var verb = bits[0];
        var code = bits.Length > 1 ? bits[1] : "";

        if (verb == "sel")
        {
            var (article, error) = await ResolveAsync(code, ct);
            if (article is null) return (error!, null);

            var source = await repository.GetSourceAsync(article.CrawlSourceId, ct);
            var preset = ContentCrawlRepository.DeserializeGuidList(source?.DefaultChannelIds);
            selections.Start(chatId, code, article.Id, preset);
            return await RenderPickerAsync(chatId, code, article.Title, ct);
        }

        var sel = selections.Get(chatId, code);
        if (sel is null)
            return ("Ô chọn đã hết hạn. Gõ <code>/dang " + Esc(code) + "</code> để mở lại.", null);

        var article2 = await repository.GetByIdAsync(sel.ArticleId, ct);
        if (article2 is null) return ("Không tìm thấy tin.", null);

        switch (verb)
        {
            case "tg":
            {
                var channels = await ActiveChannelsAsync(ct);
                if (!int.TryParse(bits.ElementAtOrDefault(2), out var idx)
                    || idx < 0 || idx >= channels.Count)
                    return ("Nút không hợp lệ", null);

                var id = channels[idx].Id;
                if (!sel.ChannelIds.Add(id)) sel.ChannelIds.Remove(id);
                return await RenderPickerAsync(chatId, code, article2.Title, ct);
            }

            case "x":
                selections.Drop(chatId, code);
                return ($"Đã huỷ. Tin <code>{Esc(code)}</code> vẫn nằm chờ duyệt.", null);

            case "go":
            {
                if (sel.ChannelIds.Count == 0)
                    return await RenderPickerAsync(chatId, code, article2.Title, ct, "⚠️ Chưa chọn page nào");

                var ids = sel.ChannelIds.ToList();
                selections.Drop(chatId, code);

                article2.TelegramChatId = chatId;
                // Ghi luôn tin nhắn đang mở để CrawlAutoPublishService SỬA CHÍNH NÓ thành kết
                // quả, thay vì gửi tin mới. Tin đã sang Approved nên không sợ đụng vai trò
                // "đã báo" của cột này ở NotifyPendingAsync (chỉ lọc Status == Pending).
                if (messageId is not null) article2.TelegramMessageId = messageId;
                await repository.UpdateAsync(article2, ct);

                var result = await pipeline.ApproveAsync(
                    article2.Id,
                    new ApproveCrawledArticleRequest { AutoPublish = true, ChannelIds = ids },
                    ct);

                return ($"⏳ <b>Đang đăng…</b>\n{Esc(article2.Title)}\n\n"
                        + $"Đang viết {result.Created} bài cho: {Esc(string.Join(", ", result.Channels))}\n"
                        + "<i>Xong sẽ báo lại ngay tại tin nhắn này.</i>", null);
            }
        }

        return ("Nút không hợp lệ", null);
    }

    /// <summary>
    /// Ô chọn mở bằng lệnh gõ tay nằm ở một tin nhắn MỚI. Ghi id của nó vào tin để lát nữa
    /// kết quả đăng sửa đúng tin nhắn đó — không ghi thì bot đẻ ra tin thứ hai và sếp phải
    /// cuộn ngược tìm xem bài nào ứng với ô chọn nào.
    /// </summary>
    public async Task RememberPickerMessageAsync(
        long chatId, string commandText, int messageId, CancellationToken ct = default)
    {
        var parts = commandText.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;

        var found = await repository.FindByShortIdAsync(parts[1], ct);
        if (found.Count != 1) return;

        found[0].TelegramChatId = chatId;
        found[0].TelegramMessageId = messageId;
        await repository.UpdateAsync(found[0], ct);
    }

    private async Task<(string, List<List<TelegramButton>>?)> RenderPickerAsync(
        long chatId, string code, string title, CancellationToken ct, string? note = null)
    {
        var sel = selections.Get(chatId, code);
        var channels = await ActiveChannelsAsync(ct);

        var sb = new StringBuilder($"<b>{Esc(title)}</b>\n\n");
        sb.AppendLine("Chọn page rồi bấm <b>Đăng</b>:");
        if (note is not null) sb.AppendLine(note);

        var rows = new List<List<TelegramButton>>();
        for (var i = 0; i < channels.Count; i += 2)
        {
            var row = new List<TelegramButton>();
            for (var j = i; j < Math.Min(i + 2, channels.Count); j++)
            {
                var picked = sel?.ChannelIds.Contains(channels[j].Id) == true;
                // Nhãn nút bị Telegram cắt khi dài; 24 ký tự vừa đủ đọc trên máy hẹp.
                row.Add(new TelegramButton(
                    (picked ? "☑ " : "☐ ") + Cut(channels[j].PageName, 24), $"tg:{code}:{j}"));
            }
            rows.Add(row);
        }
        rows.Add([new TelegramButton("🚀 Đăng", $"go:{code}"), new TelegramButton("✖️ Huỷ", $"x:{code}")]);

        return (sb.ToString().TrimEnd(), rows);
    }

    private async Task<List<(Guid Id, string PageName)>> ActiveChannelsAsync(CancellationToken ct)
    {
        // Thứ tự PHẢI ổn định giữa lúc dựng bàn phím và lúc bấm nút: callback chỉ mang chỉ số.
        // Sắp theo tên là ổn định; sắp theo ngày tạo hay không sắp thì thêm một page là lệch
        // hết chỉ số, người dùng tick "Toefl" mà đăng lên "Y Dược".
        var rows = await context.SocialChannels
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.PageName)
            .Select(c => new { c.Id, c.PageName })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.PageName)).ToList();
    }

    private async Task<string> ApproveAsync(long chatId, string arg, CancellationToken ct)
    {
        var bits = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var (article, error) = await ResolveAsync(bits.Length > 0 ? bits[0] : "", ct);
        if (article is null) return error!;

        // Phần sau mã tin là danh sách page. Bỏ trống thì dùng page mặc định của nguồn cào.
        List<Guid>? channelIds = null;
        if (bits.Length > 1)
        {
            var (ids, pageError) = await ResolvePagesAsync(bits[1], ct);
            if (ids is null) return pageError!;
            channelIds = ids;
        }

        // Ghi chat NGAY, trước khi duyệt. CrawlAutoPublishService lọc theo TelegramChatId để
        // biết báo kết quả về đâu — tin duyệt bằng /dang mà chưa từng được bot báo thì cột này
        // rỗng, vòng quét bỏ qua và bài nằm im mãi ở Approved, không ai biết.
        if (article.TelegramChatId is null)
        {
            article.TelegramChatId = chatId;
            await repository.UpdateAsync(article, ct);
        }

        // Chỉ đặt AutoPublish. Các tham số còn lại để mặc định: page mặc định của nguồn,
        // TextOnly (không sinh ảnh), template mặc định của từng page.
        var result = await pipeline.ApproveAsync(
            article.Id,
            new ApproveCrawledArticleRequest { AutoPublish = true, ChannelIds = channelIds },
            ct);

        // CỬA 1 — chỉ lên web. PHẢI nói thẳng "chưa đăng fanpage": đổi nghĩa lệnh mà giữ lời
        // thoại cũ thì sếp bấm xong tưởng đã lên fanpage, và chỉ phát hiện khi khách hỏi sao
        // chưa thấy bài. Đúng bài học từ lần đổi nhãn nút "Đăng" thành "Duyệt".
        if (result.Created == 0)
        {
            if (string.IsNullOrWhiteSpace(result.NewsUrl))
                return $"⚠️ Đã duyệt <b>{Esc(article.Title)}</b>\n"
                     + "Nhưng CHƯA lên web được — xem lý do trên giao diện.";

            return $"✅ <b>Đã lên web</b>\n{Esc(article.Title)}\n\n"
                 + $"{Esc(result.NewsUrl)}\n\n"
                 + "<i>Chưa đăng fanpage.</i> Gõ <code>/fb " + ShortId(article) + "</code> để đăng.";
        }

        var pages = string.Join(", ", result.Channels);
        return $"✅ Đã duyệt <b>{Esc(article.Title)}</b>\n"
             + $"Đang viết {result.Created} bài cho: {Esc(pages)}\n"
             + "<i>Viết xong sẽ đăng luôn và gửi link về đây.</i>";
    }

    /// <summary>
    /// CỬA 2 — đăng bài đã lên web sang fanpage.
    ///
    /// Tra theo mã của TIN ĐÃ CÀO chứ không phải mã riêng: sếp vừa nhận tin nhắn "đã lên web"
    /// kèm mã đó, bắt nhớ thêm một dãy số thứ hai là thừa.
    /// </summary>
    private async Task<string> ToFanpageAsync(string arg, CancellationToken ct)
    {
        var bits = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var (article, error) = await ResolveAnyAsync(bits.Length > 0 ? bits[0] : "", ct);
        if (article is null) return error!;

        var news = await newsRepository.GetByCrawledAsync(article.Id, ct);
        if (news is null)
            return $"Tin <code>{Esc(bits[0])}</code> chưa lên web. Gõ <code>/dang {Esc(bits[0])}</code> trước.";
        if (news.Status != Backend.Modules.NewsSite.NewsArticleStatus.Published)
            return $"Bài chưa xuất bản được: {Esc(news.ErrorMessage ?? "chưa rõ lý do")}";

        List<Guid>? channelIds = null;
        if (bits.Length > 1)
        {
            var (ids, pageError) = await ResolvePagesAsync(bits[1], ct);
            if (ids is null) return pageError!;
            channelIds = ids;
        }
        else
        {
            var source = await repository.GetSourceAsync(article.CrawlSourceId, ct);
            channelIds = ContentCrawlRepository.DeserializeGuidList(source?.DefaultChannelIds);
            if (channelIds.Count == 0)
                return "Nguồn chưa đặt page mặc định. Gõ <code>/fb " + Esc(bits[0]) + " &lt;page&gt;</code>.";
        }

        var result = await fanpage.PublishAsync(news, channelIds, autoPublish: true, ct);

        return $"📢 <b>Đang đăng fanpage</b>\n{Esc(news.Title)}\n\n"
             + $"{result.Created} bài cho: {Esc(string.Join(", ", result.Channels))}\n"
             + $"Bình luận sẽ dẫn về: {Esc(result.NewsUrl ?? news.SourceUrl)}\n\n"
             + "<i>Xong sẽ báo lại kèm link bài Facebook.</i>";
    }

    /// <summary>
    /// Tra tin theo mã, KHÔNG giới hạn trạng thái — khác ResolveAsync vốn chỉ nhận tin chờ
    /// duyệt. Lệnh /fb làm việc với tin ĐÃ duyệt nên phải tra rộng hơn.
    /// </summary>
    private async Task<(CrawledArticleModel?, string?)> ResolveAnyAsync(string arg, CancellationToken ct)
    {
        var key = (arg ?? "").Split(' ')[0].TrimStart('#');
        if (string.IsNullOrWhiteSpace(key))
            return (null, "Thiếu mã tin.");

        if (int.TryParse(key, out var code) && code > 0)
        {
            var byCode = await context.Set<CrawledArticleModel>()
                .Where(x => !x.IsDeleted && x.ShortCode == code)
                .OrderByDescending(x => x.FetchedAt)
                .FirstOrDefaultAsync(ct);
            if (byCode is not null) return (byCode, null);
        }

        var found = await repository.FindByShortIdAsync(key, ct);
        return found.Count == 1
            ? (found[0], null)
            : (null, $"Không thấy tin <code>{Esc(key)}</code>.");
    }

    private async Task<string> RejectAsync(string arg, CancellationToken ct)
    {
        var bits = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var (article, error) = await ResolveAsync(bits.Length > 0 ? bits[0] : "", ct);
        if (article is null) return error!;

        await pipeline.RejectAsync(article.Id, bits.Length > 1 ? bits[1] : "Bỏ qua từ Telegram", ct);

        await notifications.AddAsync(
            Backend.Modules.Notification.NotificationKind.ArticleRejected,
            Backend.Modules.Notification.NotificationSource.Telegram, "Telegram",
            $"Bỏ tin: {Cut(article.Title, 120)}",
            bits.Length > 1 ? bits[1] : null, "/crawl", article.Id, ct);

        return $"🗑 Đã bỏ <b>{Esc(article.Title)}</b>";
    }

    private async Task<(CrawledArticleModel?, string?)> ResolveAsync(string arg, CancellationToken ct)
    {
        var key = arg.Split(' ')[0];
        if (string.IsNullOrWhiteSpace(key))
            return (null, "Thiếu mã tin. Gõ <code>/ds</code> để xem danh sách.");

        var found = await repository.FindByShortIdAsync(key, ct);
        return found.Count switch
        {
            0 => (null, $"Không thấy tin <code>{Esc(key)}</code> đang chờ duyệt."),
            1 => (found[0], null),
            // Gõ thêm vài ký tự rẻ hơn nhiều so với đăng nhầm bài lên fanpage.
            _ => (null, $"Mã <code>{Esc(key)}</code> khớp {found.Count} tin, gõ dài thêm:\n"
                        + string.Join("\n", found.Take(5).Select(
                            x => $"<code>#{ShortId(x)}</code> {Esc(Cut(x.Title, 40))}"))),
        };
    }

    /// <summary>
    /// Tra page theo mảnh tên, ngăn bằng dấu phẩy: "/dang a3f9c1 toefl, sư phạm".
    ///
    /// So không dấu và không phân biệt hoa thường vì gõ tiếng Việt có dấu trên điện thoại
    /// giữa lúc vội là điều không nên bắt ai làm. Mảnh nào khớp nhiều page thì BÁO LỖI chứ
    /// không chọn bừa — đăng nhầm fanpage rồi gỡ vẫn còn người kịp đọc.
    /// </summary>
    private async Task<(List<Guid>?, string?)> ResolvePagesAsync(string spec, CancellationToken ct)
    {
        var channels = await context.SocialChannels
            .Where(c => !c.IsDeleted && c.IsActive)
            .Select(c => new { c.Id, c.PageName })
            .ToListAsync(ct);

        var ids = new List<Guid>();
        foreach (var raw in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var needle = VietnameseTextHelper.StripDiacritics(raw).ToLowerInvariant();
            if (needle.Length < 2) continue;

            var hits = channels
                .Where(c => VietnameseTextHelper.StripDiacritics(c.PageName)
                    .Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (hits.Count == 0)
                return (null, $"Không có page nào khớp <b>{Esc(raw)}</b>. Gõ <code>/page</code> để xem danh sách.");
            if (hits.Count > 1)
                return (null, $"<b>{Esc(raw)}</b> khớp {hits.Count} page, gõ rõ hơn:\n"
                              + string.Join("\n", hits.Take(6).Select(h => $"· {Esc(h.PageName)}")));

            if (!ids.Contains(hits[0].Id)) ids.Add(hits[0].Id);
        }

        return ids.Count == 0
            ? (null, "Không đọc được tên page nào. Gõ <code>/page</code> để xem danh sách.")
            : (ids, null);
    }

    private async Task<string> ListPagesAsync(CancellationToken ct)
    {
        var channels = await context.SocialChannels
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.PageName)
            .Select(c => c.PageName)
            .ToListAsync(ct);

        if (channels.Count == 0) return "Chưa kết nối page nào.";

        var sb = new StringBuilder($"<b>{channels.Count} page đang bật</b>\n");
        foreach (var name in channels) sb.AppendLine($"· {Esc(name)}");
        sb.AppendLine("\nGõ một phần tên là đủ: <code>/dang 3 toefl, su pham</code>");
        return sb.ToString().TrimEnd();
    }

    private async Task<string> ListPendingAsync(CancellationToken ct)
    {
        await repository.EnsureShortCodesAsync(ct);
        var pending = await repository.GetPendingAsync(30, ct);
        if (pending.Count == 0) return "Không có tin nào chờ duyệt.";

        var sb = new StringBuilder($"<b>{pending.Count} tin chờ duyệt</b>\n");
        foreach (var a in pending.Take(10))
            sb.AppendLine($"<code>#{ShortId(a)}</code> {(a.QualityScore is int q ? $"{ScoreIcon(q)}{q} " : "")}"
                          + Esc(Cut(a.Title, 46)));
        sb.AppendLine("\nDuyệt: <code>/dang &lt;mã&gt;</code> · Bỏ: <code>/bo &lt;mã&gt;</code>");
        return sb.ToString().TrimEnd();
    }

    private async Task<string> StatusAsync(CancellationToken ct)
    {
        var counts = await repository.CountByStatusAsync(ct);
        var sources = await repository.GetSourcesAsync(false, ct);
        var runs = await repository.GetRecentRunsAsync(null, 1, ct);

        var sb = new StringBuilder("<b>Tình trạng</b>\n");
        foreach (var (status, n) in counts.OrderBy(x => x.Key))
            sb.AppendLine($"{Label(status)}: {n}");
        sb.AppendLine($"\nNguồn: {sources.Count(s => s.IsActive)} đang bật / {sources.Count}");
        if (runs.Count > 0)
            sb.AppendLine($"Lượt cào gần nhất: {runs[0].StartedAt:dd/MM HH:mm} UTC · {runs[0].ItemsNew} bài mới");
        return sb.ToString().TrimEnd();
    }

    private async Task<string> CrawlNowAsync(CancellationToken ct)
    {
        var sources = await repository.GetSourcesAsync(true, ct);
        if (sources.Count == 0) return "Chưa có nguồn nào đang bật.";

        var sb = new StringBuilder();
        foreach (var s in sources)
        {
            // Tuần tự: OpenClaw chỉ có MỘT tab, hai nguồn cùng navigate sẽ đè lên nhau.
            var run = await pipeline.RunSourceAsync(s.Id, "telegram", ct);
            sb.AppendLine($"{Esc(s.Name)}: {run.ItemsNew} mới / {run.ItemsFetched} lấy về");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Label(CrawledArticleStatus s) => s switch
    {
        CrawledArticleStatus.Pending => "Chờ duyệt",
        CrawledArticleStatus.Duplicate => "Trùng",
        CrawledArticleStatus.Filtered => "Bị lọc",
        CrawledArticleStatus.Approved => "Đã duyệt",
        CrawledArticleStatus.Rejected => "Đã bỏ",
        CrawledArticleStatus.Failed => "Lỗi",
        _ => s.ToString(),
    };

    private static string Cut(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    /// <summary>
    /// Lời chào /start. Khác /help ở chỗ nói VIỆC BOT LÀM trước, danh sách lệnh sau — người
    /// gõ /start là người chưa biết bot này để làm gì, đưa ngay bảng lệnh thì đọc xong vẫn
    /// không hiểu bấm vào sẽ xảy ra chuyện gì.
    ///
    /// Có số liệu thật (bao nhiêu tin đang chờ, mấy nguồn đang bật) vì đó là câu đầu tiên
    /// ai cũng muốn biết, và nó chứng minh luôn bot đang nối được với hệ thống.
    /// </summary>
    private async Task<string> StartAsync(CancellationToken ct)
    {
        var counts = await repository.CountByStatusAsync(ct);
        var pending = counts.GetValueOrDefault(CrawledArticleStatus.Pending);
        var sources = await repository.GetSourcesAsync(true, ct);

        var sb = new StringBuilder();
        sb.AppendLine("👋 <b>Bot duyệt tin VNI</b>");
        sb.AppendLine();
        sb.AppendLine("Hệ thống tự cào tin giáo dục từ báo, lọc trùng, rồi đẩy về đây để duyệt.");
        sb.AppendLine("Duyệt một cái là bài tự viết riêng cho từng page rồi đăng thẳng lên Facebook.");
        sb.AppendLine();
        sb.AppendLine("<b>Cách dùng</b>");
        sb.AppendLine("1. Tin mới cào xong sẽ tự hiện ở đây, kèm ảnh và 3 nút.");
        sb.AppendLine("2. Bấm <b>✅ Đưa lên web</b> — AI viết bài rồi đăng lên tintuc.vni.edu.vn.");
        sb.AppendLine("3. Bot báo lại link bài ngay tại tin nhắn đó để bấm vào xem.");
        sb.AppendLine();
        sb.AppendLine("Đăng lên fanpage là bước RIÊNG — xem bài trên web ưng rồi mới gõ");
        sb.AppendLine("<code>/fb &lt;mã&gt;</code>. Nhờ vậy bài Facebook dẫn về web của mình,");
        sb.AppendLine("không phải link báo gốc.");
        sb.AppendLine();
        sb.AppendLine($"Hiện có <b>{pending}</b> tin chờ duyệt · {sources.Count} nguồn đang bật.");
        sb.AppendLine();
        sb.AppendLine("Gõ <code>/ds</code> để xem danh sách, <code>/help</code> để xem đủ lệnh.");
        return sb.ToString().TrimEnd();
    }

    private static string Help() =>
        """
        <b>Lệnh</b>

        <code>/ds</code> — danh sách tin đang chờ duyệt kèm mã
        <code>/dang &lt;mã&gt;</code> — mở ô chọn page rồi đăng
        <code>/dang &lt;mã&gt; &lt;page&gt;</code> — đăng thẳng, bỏ qua ô chọn
            ví dụ: <code>/dang 3 toefl, su pham</code>
            gõ một phần tên là đủ, không cần dấu
        <code>/bo &lt;mã&gt; [lý do]</code> — bỏ tin, không đăng
        <code>/page</code> — các page đang bật
        <code>/cao</code> — cào ngay, không đợi tới giờ
        <code>/tt</code> — tình trạng hệ thống

        <b>Lưu ý</b>
        · Mã tin là số nhỏ (1, 2, 3…) lấy ở <code>/ds</code> hoặc ngay trên thẻ tin.
        · Số được cấp lại cho tin mới sau khi tin cũ đã duyệt hoặc bỏ, nên cứ xem
          <code>/ds</code> trước khi gõ.
        · Cùng một tin đăng lên nhiều page sẽ được viết lại theo góc khác nhau,
          không phải copy cùng một bài.
        · Bài cào đăng dạng nền màu, link bài gốc nằm ở bình luận đầu tiên.
        · Muốn xem lại trước khi đăng, đổi template hay sinh ảnh: duyệt trên web.
        """;
}
