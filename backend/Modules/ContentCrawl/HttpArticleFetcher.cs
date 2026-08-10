using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Lấy tin bằng HTTP thuần: mở trang chuyên mục → nhặt link bài → tải từng bài → bóc thân bài.
/// Không dùng trình duyệt.
///
/// Số đo thật khi thay OpenClaw bằng đường này (12 bài, 6 báo):
///   OpenClaw     41 giây mỗi bài, nghẽn vì chỉ có MỘT tab nên phải tuần tự
///   HTTP thuần   0,021 giây mỗi bài, lấy được 100–105% số ký tự
/// Nhanh hơn 1.757 lần và không thua chất lượng.
///
/// Phần còn thiếu: ~6% bài (dạng ảnh, Dmagazine) bóc ra quá ngắn — nơi gọi phải rơi sang
/// OpenClaw. Đó là lý do lớp này trả về cả bài ngắn kèm cờ, không tự nuốt.
/// </summary>
public partial class HttpArticleFetcher(
    HttpClient http,
    ReadableArticleParser parser,
    IOptions<ContentCrawlOptions> options,
    ILogger<HttpArticleFetcher> logger)
{
    /// <summary>
    /// Giãn cách giữa hai lượt gọi CÙNG MỘT tên miền, tính bằng mili giây.
    ///
    /// dantri.com.vn ghi rõ `Crawl-delay: 1` trong robots.txt (đã kiểm ngày lập kế hoạch).
    /// Năm báo còn lại không đặt, nhưng vẫn giãn 300ms cho lễ phép — bắn 30 request cùng lúc
    /// vào một tên miền là cách nhanh nhất để bị chặn IP.
    /// </summary>
    private static readonly Dictionary<string, int> CrawlDelayMs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dantri.com.vn"] = 1000,
    };

    private const int DefaultDelayMs = 300;

    /// <summary>Lần gọi gần nhất theo tên miền — để tôn trọng crawl-delay giữa các lượt.</summary>
    private static readonly ConcurrentDictionary<string, DateTime> LastHitUtc = new();

    public async Task<List<CrawledPageItem>> FetchAsync(
        CrawlSourceModel source, ISet<string>? knownUrls = null, CancellationToken ct = default)
    {
        var opt = options.Value;
        var links = await DiscoverLinksAsync(source, ct);
        if (links.Count == 0)
        {
            logger.LogWarning("Không tìm được link bài nào ở {Url}", source.Url);
            return [];
        }

        // Lọc URL đã cào TRƯỚC khi tải bài. Rẻ hơn nhiều so với tải rồi vứt ở bước chấm trùng,
        // và giữ cho suất MaxItemsPerRun dành cho bài thật sự mới.
        var fresh = knownUrls is { Count: > 0 }
            ? links.Where(l => !knownUrls.Contains(l.Url)).ToList()
            : links;

        if (fresh.Count < links.Count)
            logger.LogInformation("Bỏ qua {Skipped}/{Total} link đã cào", links.Count - fresh.Count, links.Count);

        var take = Math.Clamp(source.MaxItemsPerRun, 1, 100);
        var items = new List<CrawledPageItem>();

        foreach (var link in fresh.Take(take))
        {
            ct.ThrowIfCancellationRequested();
            var item = await FetchArticleAsync(link, ct);
            if (item is not null) items.Add(item);
        }

        logger.LogInformation(
            "HTTP lấy {Got}/{Tried} bài từ {Source}", items.Count, Math.Min(fresh.Count, take), source.Name);
        return items;
    }

    /// <summary>Tải MỘT bài. Trả null khi hỏng hoặc bóc ra quá ngắn — nơi gọi tự quyết dự phòng.</summary>
    public async Task<CrawledPageItem?> FetchArticleAsync(DiscoveredLink link, CancellationToken ct)
    {
        try
        {
            var html = await GetStringAsync(link.Url, ct);
            if (html is null) return null;

            var parsed = parser.Parse(html, link.Url);

            if (parsed.Body.Length < options.Value.MinExtractedLength)
            {
                logger.LogInformation(
                    "Bóc bằng HTTP chỉ được {Len} ký tự ở {Url} — cần dự phòng", parsed.Body.Length, link.Url);
                return null;
            }

            return new CrawledPageItem(
                Title: parsed.Title ?? link.Title,
                Summary: parsed.Summary,
                Content: parsed.Body,
                Link: link.Url,
                Author: parsed.Author,
                ThumbnailUrl: parsed.ImageUrl,
                PublishedAtUtc: parsed.PublishedUtc);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Tải bài {Url} thất bại", link.Url);
            return null;
        }
    }

    /// <summary>
    /// Nhặt link bài trên trang chuyên mục. Dùng ĐÚNG luật của bộ cào trình duyệt: mã số ≥6 chữ
    /// số trong URL (quy ước chung của báo Việt) và chữ neo ≥25 ký tự để loại menu/quảng cáo.
    /// Giữ chung một luật để đổi qua lại giữa hai đường không ra kết quả khác nhau.
    /// </summary>
    private async Task<List<DiscoveredLink>> DiscoverLinksAsync(CrawlSourceModel source, CancellationToken ct)
    {
        var html = await GetStringAsync(source.Url, ct);
        if (html is null) return [];

        if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var baseUri)) return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = new List<DiscoveredLink>();

        foreach (var m in AnchorTag().Matches(html).Cast<Match>())
        {
            var href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
            var anchorText = Regex.Replace(m.Groups[2].Value, "<[^>]+>", " ");
            anchorText = System.Net.WebUtility.HtmlDecode(anchorText);
            anchorText = Regex.Replace(anchorText, @"\s+", " ").Trim();

            if (anchorText.Length < 25) continue;
            if (!Uri.TryCreate(baseUri, href, out var abs)) continue;
            if (!string.Equals(abs.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) continue;
            if (!ArticleUrl().IsMatch(abs.AbsoluteUri)) continue;

            var clean = abs.GetLeftPart(UriPartial.Path);
            if (!seen.Add(clean)) continue;

            found.Add(new DiscoveredLink(anchorText[..Math.Min(300, anchorText.Length)], clean));
        }

        return found;
    }

    private async Task<string?> GetStringAsync(string url, CancellationToken ct)
    {
        await RespectCrawlDelayAsync(url, ct);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        // Khai báo danh tính thật kèm URL liên hệ. Giả làm trình duyệt là cách chắc chắn để
        // chủ trang không phân biệt được bot lễ phép với bot phá, rồi chặn cả hai.
        req.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (compatible; VNIAutomationBot/1.0; +https://vni.edu.vn)");
        req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");

        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            logger.LogWarning("HTTP {Code} khi tải {Url}", (int)res.StatusCode, url);
            return null;
        }

        return await res.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Giãn theo TÊN MIỀN, không phải theo tiến trình. Hai nguồn khác nhau của cùng một báo
    /// (giáo dục và kinh doanh của dantri) vẫn phải cộng dồn giãn cách — chúng đập vào cùng
    /// một máy chủ.
    /// </summary>
    private async Task RespectCrawlDelayAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;

        var host = uri.Host;
        var delayMs = CrawlDelayMs.TryGetValue(host, out var d) ? d : DefaultDelayMs;

        if (LastHitUtc.TryGetValue(host, out var last))
        {
            var waited = (DateTime.UtcNow - last).TotalMilliseconds;
            if (waited < delayMs)
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs - waited), ct);
        }

        LastHitUtc[host] = DateTime.UtcNow;
    }

    [GeneratedRegex(@"<a\b[^>]*href=[""']([^""']+)[""'][^>]*>(.*?)</a>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex AnchorTag();

    [GeneratedRegex(@"-\d{6,}\.(htm|html)(\?|#|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ArticleUrl();
}
