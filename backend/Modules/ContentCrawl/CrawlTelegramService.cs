using System.Net;
using System.Text;
using Backend.Modules.ContentCrawl.Enums;
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
public class CrawlTelegramService(
    ContentCrawlRepository repository,
    ContentCrawlPipelineService pipeline,
    TelegramClient telegram,
    IOptions<TelegramOptions> options,
    ILogger<CrawlTelegramService> logger)
{
    /// <summary>Mã ngắn hiện cho người dùng gõ — 6 ký tự hex đầu của Guid.</summary>
    public static string ShortId(Guid id) => id.ToString("N")[..6].ToLowerInvariant();

    // ── Báo tin mới ─────────────────────────────────────────────────────────

    /// <summary>
    /// Gửi tin chờ duyệt chưa báo lần nào. Ghi TelegramMessageId ngay sau khi gửi để lần
    /// quét sau không báo lại — đây là toàn bộ cơ chế chống gửi trùng, không có gì khác.
    /// </summary>
    public async Task<int> NotifyPendingAsync(long chatId, CancellationToken ct = default)
    {
        var articles = await repository.GetUnnotifiedPendingAsync(options.Value.MaxNotifyPerTick, ct);
        if (articles.Count == 0) return 0;

        var sent = 0;
        foreach (var a in articles)
        {
            ct.ThrowIfCancellationRequested();
            var code = ShortId(a.Id);
            var messageId = await telegram.SendMessageAsync(
                chatId, FormatArticle(a),
                [new TelegramButton("🚀 Duyệt & đăng", $"ok:{code}"), new TelegramButton("🗑 Bỏ", $"no:{code}")],
                ct);

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

    private static string FormatArticle(CrawledArticleModel a)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<b>{Esc(a.Title)}</b>");
        sb.AppendLine();
        var meta = new List<string> { $"<code>{ShortId(a.Id)}</code>" };
        if (!string.IsNullOrWhiteSpace(a.SourceCategory)) meta.Add(Esc(a.SourceCategory));
        if (!string.IsNullOrWhiteSpace(a.Content)) meta.Add($"{a.Content.Length:N0} chữ");
        sb.AppendLine(string.Join(" · ", meta));
        if (!string.IsNullOrWhiteSpace(a.SourceUrl)) sb.AppendLine(Esc(a.SourceUrl));
        return sb.ToString().TrimEnd();
    }

    private static string Esc(string? s) => WebUtility.HtmlEncode(s ?? "");

    // ── Xử lý lệnh ──────────────────────────────────────────────────────────

    /// <summary>Trả về câu trả lời để bot gửi lại. Không bao giờ ném — lỗi thành lời nhắn.</summary>
    public async Task<string> HandleCommandAsync(long chatId, string text, CancellationToken ct = default)
    {
        var parts = text.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return Help();

        // "/dang@vni_auto_bot" trong group — Telegram tự gắn tên bot vào lệnh.
        var cmd = parts[0].ToLowerInvariant().TrimStart('/').Split('@')[0];
        var arg = parts.Length > 1 ? parts[1].Trim() : "";

        try
        {
            return cmd switch
            {
                "dang" or "duyet" => await ApproveAsync(chatId, arg, ct),
                "bo" or "loai" => await RejectAsync(arg, ct),
                "ds" or "list" => await ListPendingAsync(ct),
                "tt" or "status" => await StatusAsync(ct),
                "cao" or "crawl" => await CrawlNowAsync(ct),
                "start" or "help" => Help(),
                _ => $"Không hiểu lệnh <code>/{Esc(cmd)}</code>.\n\n{Help()}",
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Lệnh Telegram '{Text}' lỗi", text);
            return $"⚠️ Lỗi: {Esc(ex.Message)}";
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

    private async Task<string> ApproveAsync(long chatId, string arg, CancellationToken ct)
    {
        var (article, error) = await ResolveAsync(arg, ct);
        if (article is null) return error!;

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
            article.Id, new ApproveCrawledArticleRequest { AutoPublish = true }, ct);

        // Không hứa "đã đăng" ở đây: sinh nội dung chạy bất đồng bộ, mỗi page vài chục giây.
        // CrawlAutoPublishService sẽ đăng rồi nhắn link về trong một tin nhắn riêng.
        var pages = string.Join(", ", result.Channels);
        return $"✅ Đã duyệt <b>{Esc(article.Title)}</b>\n"
             + $"Đang viết {result.Created} bài cho: {Esc(pages)}\n"
             + "<i>Viết xong sẽ đăng luôn và gửi link về đây.</i>";
    }

    private async Task<string> RejectAsync(string arg, CancellationToken ct)
    {
        var bits = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var (article, error) = await ResolveAsync(bits.Length > 0 ? bits[0] : "", ct);
        if (article is null) return error!;

        await pipeline.RejectAsync(article.Id, bits.Length > 1 ? bits[1] : "Bỏ qua từ Telegram", ct);
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
                            x => $"<code>{ShortId(x.Id)}</code> {Esc(Cut(x.Title, 40))}"))),
        };
    }

    private async Task<string> ListPendingAsync(CancellationToken ct)
    {
        var pending = await repository.GetPendingAsync(30, ct);
        if (pending.Count == 0) return "Không có tin nào chờ duyệt.";

        var sb = new StringBuilder($"<b>{pending.Count} tin chờ duyệt</b>\n");
        foreach (var a in pending.Take(10))
            sb.AppendLine($"<code>{ShortId(a.Id)}</code> {Esc(Cut(a.Title, 52))}");
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

    private static string Help() =>
        """
        <b>Lệnh</b>
        <code>/ds</code> — tin đang chờ duyệt
        <code>/dang &lt;mã&gt;</code> — duyệt, viết bài rồi ĐĂNG LUÔN, xong gửi link về
        <code>/bo &lt;mã&gt; [lý do]</code> — bỏ tin
        <code>/cao</code> — cào ngay, không đợi lịch
        <code>/tt</code> — tình trạng hệ thống

        Muốn đổi page, template, sinh ảnh hay xem lại trước khi đăng thì duyệt trên web.
        """;
}
