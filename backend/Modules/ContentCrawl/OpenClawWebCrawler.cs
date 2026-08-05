using System.Text.Json;
using Backend.Shared.OpenClaw;
using Microsoft.Extensions.Options;

namespace Backend.Modules.ContentCrawl;

/// <summary>Một bài đã cào kèm TOÀN VĂN.</summary>
public sealed record CrawledPageItem(
    string Title,
    string? Summary,
    string? Content,
    string Link,
    string? Author,
    string? ThumbnailUrl,
    DateTime? PublishedAtUtc);

/// <summary>
/// Cào web bằng trình duyệt OpenClaw: mở trang chuyên mục → lấy link bài → mở từng bài →
/// bóc toàn văn.
///
/// Khác RSS ở chỗ quyết định: RSS chỉ cho ~50 từ tóm tắt nên AI phải bịa cho đủ bài;
/// ở đây lấy được 1.600–5.700 ký tự thật (đo trên VnExpress/Dân trí/Thanh Niên).
/// </summary>
public class OpenClawWebCrawler(
    IOpenClawBrowserClient browser,
    IOptions<OpenClawOptions> openClawOptions,
    ILogger<OpenClawWebCrawler> logger)
{
    /// <summary>
    /// Tìm link bài trên trang chuyên mục. Nhận diện theo mã số dài trong URL — quy ước
    /// chung của báo Việt Nam (dantri ...-20260805110502599.htm, vnexpress ...-5105261.html).
    /// Kèm ràng buộc độ dài chữ neo để loại link menu/quảng cáo.
    /// </summary>
    private const string DiscoverLinksJs = """
        () => {
          const seen = new Set(), out = [];
          const origin = location.origin;
          document.querySelectorAll('a[href]').forEach((a) => {
            const h = a.href;
            if (!h.startsWith(origin)) return;
            if (!/-\d{6,}\.(htm|html)(\?|#|$)/i.test(h)) return;
            const clean = h.split('#')[0].split('?')[0];
            if (seen.has(clean)) return;
            const t = (a.innerText || '').trim().replace(/\s+/g, ' ');
            if (t.length < 25) return;
            seen.add(clean);
            out.push({ title: t.slice(0, 300), url: clean });
          });
          return out;
        }
        """;

    /// <summary>
    /// Bóc thân bài. Hai bài học đã trả giá:
    ///  1. KHÔNG chặn theo tên class ở cả chuỗi tổ tiên — VnExpress đặt thân bài trong
    ///     div.sidebar-1, chặn theo chữ "sidebar" là giết nguyên bài mà không báo lỗi.
    ///  2. Ưu tiên thẻ &lt;article&gt; rồi mới chấm mật độ chữ — nếu chỉ chấm mật độ thì
    ///     khu bình luận (thường nhiều chữ hơn bài) sẽ thắng.
    /// </summary>
    private const string ExtractArticleJs = """
        () => {
          const meta = (s) => document.querySelector(s)?.content || '';
          const BLOCK_TAGS = ['nav','aside','footer','header','form','figcaption','figure'];
          const BLOCK_WORDS = ['comment','binh-luan','binhluan','reply','related','lien-quan',
            'tin-lien-quan','advert','quang-cao','newsletter','subscribe','most-read',
            'trending-news','box-tinlienquan','author-info','tag-list'];
          const hay = (n) => `${n.id || ''} ${typeof n.className === 'string' ? n.className : ''}`.toLowerCase();
          const isNoise = (el) => {
            for (let n = el, i = 0; n && n !== document.body && i < 12; n = n.parentElement, i++) {
              if (BLOCK_TAGS.includes(n.tagName.toLowerCase())) return true;
              if (BLOCK_WORDS.some((w) => hay(n).includes(w))) return true;
            }
            return false;
          };
          const textOf = (root) => Array.from(root.querySelectorAll('p'))
            .filter((p) => !isNoise(p) && p.innerText.trim().length > 40)
            .map((p) => p.innerText.trim());

          let best = null;
          const named = Array.from(document.querySelectorAll(
            'article, [itemprop="articleBody"], [class*="fck_detail"], [class*="singular-content"], [class*="detail-content"]'
          )).filter((el) => !isNoise(el));
          if (named.length) {
            let bestLen = 0;
            named.forEach((el) => { const len = textOf(el).join(' ').length; if (len > bestLen) { bestLen = len; best = el; } });
          }
          if (!best) {
            const score = new Map();
            Array.from(document.querySelectorAll('p'))
              .filter((p) => !isNoise(p) && p.innerText.trim().length > 60)
              .forEach((p) => { let n = p.parentElement;
                for (let i = 0; i < 4 && n && n !== document.body; i++) {
                  score.set(n, (score.get(n) || 0) + p.innerText.trim().length); n = n.parentElement; } });
            let bs = 0; score.forEach((v, k) => { if (v > bs) { bs = v; best = k; } });
          }

          const seen = new Set();
          const body = (best ? textOf(best) : []).filter((t) => {
            if (/^(Ảnh|Nguồn|Xem thêm|Video|Đọc thêm)\s*[:：]/i.test(t)) return false;
            if (/^(Trả lời|Thích|Chia sẻ|Báo xấu)$/i.test(t)) return false;
            const k = t.slice(0, 60);
            if (seen.has(k)) return false;
            seen.add(k); return true;
          });

          return {
            title: (document.querySelector('h1')?.innerText || meta('meta[property="og:title"]')).trim(),
            summary: meta('meta[property="og:description"]').trim(),
            content: body.join('\n\n'),
            image: meta('meta[property="og:image"]'),
            author: meta('meta[name="author"]') || meta('meta[property="article:author"]'),
            published: meta('meta[property="article:published_time"]') || meta('meta[itemprop="datePublished"]')
          };
        }
        """;

    public async Task<List<CrawledPageItem>> FetchAsync(
        CrawlSourceModel source, CancellationToken ct = default)
    {
        if (!browser.IsConfigured)
            throw new OpenClawException(
                "OpenClaw chưa sẵn sàng. Bật OpenClaw:Enabled và điền OpenClaw:Token.");

        var links = await DiscoverLinksAsync(source, ct);
        if (links.Count == 0)
        {
            logger.LogWarning("Không tìm được link bài nào ở {Url} — kiểm tra URL có phải trang chuyên mục không", source.Url);
            return [];
        }

        var take = Math.Clamp(source.MaxItemsPerRun, 1, 100);
        var items = new List<CrawledPageItem>();
        var delay = Math.Max(0, openClawOptions.Value.DelayBetweenPagesMs);

        foreach (var link in links.Take(take))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var item = await ExtractArticleAsync(link, ct);
                if (item is not null) items.Add(item);
            }
            catch (OpenClawException ex)
            {
                // Một bài hỏng không được làm chết cả lượt cào.
                logger.LogWarning(ex, "Bỏ qua bài {Url}", link.Url);
            }

            if (delay > 0) await Task.Delay(delay, ct);
        }

        logger.LogInformation("Cào {Source}: thấy {Found} link, bóc được {Got} bài toàn văn",
            source.Name, links.Count, items.Count);
        return items;
    }

    private async Task<List<DiscoveredLink>> DiscoverLinksAsync(CrawlSourceModel source, CancellationToken ct)
    {
        await browser.NavigateAsync(source.Url, ct);
        var eval = await browser.EvaluateAsync(DiscoverLinksJs, ct);

        if (!SameArticle(eval.Url, source.Url))
            logger.LogWarning("Trang chuyên mục chuyển hướng: yêu cầu {Want} nhưng đang ở {Got}", source.Url, eval.Url);

        if (eval.Result.ValueKind != JsonValueKind.Array) return [];
        return eval.Result.EnumerateArray()
            .Select(e => new DiscoveredLink(
                e.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                e.TryGetProperty("url", out var u) ? u.GetString() ?? "" : ""))
            .Where(l => !string.IsNullOrWhiteSpace(l.Url))
            .ToList();
    }

    private async Task<CrawledPageItem?> ExtractArticleAsync(DiscoveredLink link, CancellationToken ct)
    {
        await browser.NavigateAsync(link.Url, ct);
        var eval = await browser.EvaluateAsync(ExtractArticleJs, ct);

        // CHỐT CHẶN quan trọng nhất: URL thật phải khớp bài đang yêu cầu. Không khớp nghĩa là
        // trang chưa tải xong hoặc bị chuyển hướng — bóc tiếp sẽ lấy nội dung bài TRƯỚC ĐÓ,
        // sai hoàn toàn mà không có lỗi nào báo ra. Đã dính bẫy này 2 lần lúc thử nghiệm.
        if (!SameArticle(eval.Url, link.Url))
        {
            logger.LogWarning("Bỏ bài: yêu cầu {Want} nhưng trình duyệt đang ở {Got}", link.Url, eval.Url);
            return null;
        }

        if (eval.Result.ValueKind != JsonValueKind.Object) return null;
        string? Str(string name) => eval.Result.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

        var title = Str("title");
        if (string.IsNullOrWhiteSpace(title)) title = link.Title;
        if (string.IsNullOrWhiteSpace(title)) return null;

        var content = Str("content");
        if (string.IsNullOrWhiteSpace(content))
            logger.LogWarning("Bóc được tiêu đề nhưng KHÔNG có thân bài: {Url}", link.Url);

        return new CrawledPageItem(
            Title: title.Trim(),
            Summary: Str("summary"),
            Content: content,
            Link: link.Url,
            Author: Str("author"),
            ThumbnailUrl: Str("image"),
            PublishedAtUtc: ParseDate(Str("published")));
    }

    /// <summary>So sánh bỏ query/fragment/dấu gạch cuối — báo hay thêm utm khi điều hướng.</summary>
    private static bool SameArticle(string? a, string? b)
    {
        static string Norm(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return string.Empty;
            var s = v.Split('#')[0].Split('?')[0].TrimEnd('/');
            return s.Replace("https://", "").Replace("http://", "").ToLowerInvariant();
        }
        var x = Norm(a); var y = Norm(b);
        return x.Length > 0 && y.Length > 0 && (x == y || x.EndsWith(y) || y.EndsWith(x));
    }

    private static DateTime? ParseDate(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? null
            : DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces, out var dto)
                ? dto.UtcDateTime : null;

    private sealed record DiscoveredLink(string Title, string Url);
}
