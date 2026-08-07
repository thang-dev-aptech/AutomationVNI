using System.Web;
using Backend.Modules.ContentCrawl.Enums;
using Backend.Shared.OpenClaw;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Lấy tin từ feed: RSS của một báo, hoặc Google News RSS theo từ khoá.
///
/// Chia vai rõ ràng, và đây là điểm mấu chốt của cả kiến trúc:
///   FEED  → phát hiện tin mới (tiêu đề, link, tóm tắt, ảnh) — rẻ, phủ rộng
///   HTTP  → tải toàn văn từng bài — 0,021 giây/bài
///
/// Vì sao không dừng ở feed: tóm tắt RSS đo trên 1.480 bài chỉ dài trung bình 171 ký tự.
/// Bắt AI viết bài hoàn chỉnh cho website từ 171 ký tự là ép nó bịa. Phải có toàn văn.
///
/// Vì sao không bỏ feed mà chỉ dùng HTTP: trang chuyên mục chỉ hiện ~20-30 link mới nhất,
/// còn feed cho 60-100 (vietnamnet tới 1.000), và Google News gom được 38-61 báo trong MỘT
/// truy vấn mà không phải khai từng feed.
/// </summary>
public class FeedSourceFetcher(
    RssFeedReader rssReader,
    HttpArticleFetcher httpFetcher,
    IOpenClawBrowserClient browser,
    ILogger<FeedSourceFetcher> logger)
{
    /// <summary>
    /// Google News chỉ trả mô tả 14 ký tự nên bắt buộc phải tải toàn văn.
    /// Đường dẫn cố định hl/gl/ceid = tiếng Việt, Việt Nam — bỏ thì trả tin tiếng Anh.
    /// </summary>
    private const string GoogleNewsBase = "https://news.google.com/rss/search";

    public static string BuildFeedUrl(CrawlSourceModel source)
        => source.SourceType == CrawlSourceType.GoogleNews
            ? $"{GoogleNewsBase}?q={HttpUtility.UrlEncode(source.Url)}&hl=vi&gl=VN&ceid=VN:vi"
            : source.Url;

    /// <summary>
    /// Đổi link mờ của Google News sang địa chỉ bài gốc.
    ///
    /// Bắt buộc phải làm, và KHÔNG chỉ vì tiện: Google mã hoá URL thật bằng protobuf từ 2024
    /// nên không giải mã được, và mỗi truy vấn sinh ra một mã khác nhau cho CÙNG một bài. Lưu
    /// nguyên link mờ thì cùng một bài đến từ Google News và từ feed của báo sẽ KHÔNG bị nhận
    /// ra là trùng — bậc chống trùng theo URL mất tác dụng hoàn toàn.
    ///
    /// Đã thử: chuyển hướng của Google được giải bằng JavaScript, nên HTTP thuần theo tới đâu
    /// vẫn quẩn ở news.google.com. Chỉ trình duyệt thật mới ra được báo gốc (đã kiểm: ra
    /// tcnnld.vn/news/detail/72912/...).
    ///
    /// Giá: ~41 giây mỗi link. Đó là cái giá của việc dùng Google News, và là lý do nên đặt
    /// MaxItemsPerRun nhỏ cho loại nguồn này. OpenClaw tắt thì giữ nguyên link mờ và đi tiếp —
    /// tin vẫn hiện cho người duyệt, chỉ là chưa có toàn văn.
    /// </summary>
    private async Task<string> ResolveGoogleLinkAsync(string googleUrl, CancellationToken ct)
    {
        if (!browser.IsConfigured) return googleUrl;

        try
        {
            await browser.NavigateAsync(googleUrl, ct);

            // PHẢI hỏi lại location.href. /navigate trả về URL ĐÃ YÊU CẦU chứ không phải URL
            // đích — đã kiểm: nó trả nguyên link news.google.com. Google chuyển hướng bằng
            // JavaScript SAU khi trang tải xong, nên phải chờ rồi đọc địa chỉ thật của tab.
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            var eval = await browser.EvaluateAsync("() => location.href", ct);
            var landed = eval.Result.ValueKind == System.Text.Json.JsonValueKind.String
                ? eval.Result.GetString()
                : eval.Url;

            if (!string.IsNullOrWhiteSpace(landed)
                && !landed.Contains("news.google.com", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Gỡ link Google News → {Url}", landed);
                return landed;
            }

            logger.LogDebug("Không gỡ được link Google News, vẫn ở {Url}", landed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Gỡ link Google News lỗi");
        }

        return googleUrl;
    }

    public async Task<List<CrawledPageItem>> FetchAsync(
        CrawlSourceModel source, ISet<string>? knownUrls = null, CancellationToken ct = default)
    {
        var feedUrl = BuildFeedUrl(source);

        // Xin dư gấp 3 lần suất: phần lớn bài trong feed đã cào rồi, lọc xong mới còn đủ.
        var wanted = Math.Clamp(source.MaxItemsPerRun, 1, 100);
        var entries = await rssReader.FetchAsync(feedUrl, wanted * 3, ct);
        if (entries.Count == 0)
        {
            logger.LogWarning("Feed {Url} không trả bài nào", feedUrl);
            return [];
        }

        var fresh = knownUrls is { Count: > 0 }
            ? entries.Where(e => !knownUrls.Contains(e.Link)).ToList()
            : entries;

        if (fresh.Count < entries.Count)
            logger.LogInformation("Bỏ qua {Skipped}/{Total} bài đã cào", entries.Count - fresh.Count, entries.Count);

        var items = new List<CrawledPageItem>();
        var noBody = 0;

        foreach (var entry in fresh.Take(wanted))
        {
            ct.ThrowIfCancellationRequested();

            var link = source.SourceType == CrawlSourceType.GoogleNews
                ? await ResolveGoogleLinkAsync(entry.Link, ct)
                : entry.Link;

            var full = await httpFetcher.FetchArticleAsync(new DiscoveredLink(entry.Title, link), ct);

            if (full is not null)
            {
                // Ưu tiên dữ liệu của FEED cho phần siêu dữ liệu: tiêu đề trong feed đã được
                // toà soạn biên tập, còn <h1> trên trang đôi khi kèm nhãn chuyên mục. Ảnh và
                // ngày đăng thì ngược lại — thẻ og trên trang bài chuẩn hơn media:content.
                items.Add(full with
                {
                    Link = link,
                    Title = string.IsNullOrWhiteSpace(entry.Title) ? full.Title : entry.Title,
                    Summary = full.Summary ?? entry.Summary,
                    ThumbnailUrl = full.ThumbnailUrl ?? entry.ThumbnailUrl,
                    PublishedAtUtc = full.PublishedAtUtc ?? entry.PublishedAtUtc,
                    Author = full.Author ?? entry.Author,
                });
                continue;
            }

            // Tải toàn văn hỏng thì vẫn giữ tin, chỉ có tóm tắt. Vứt luôn là mất tin trong
            // im lặng; giữ lại thì người duyệt còn thấy và quyết được.
            noBody++;
            items.Add(new CrawledPageItem(
                Title: entry.Title,
                Summary: entry.Summary,
                Content: null,
                Link: link,
                Author: entry.Author,
                ThumbnailUrl: entry.ThumbnailUrl,
                PublishedAtUtc: entry.PublishedAtUtc));
        }

        logger.LogInformation(
            "Feed {Source}: {Total} bài ({NoBody} bài chưa lấy được toàn văn)",
            source.Name, items.Count, noBody);

        return items;
    }
}
