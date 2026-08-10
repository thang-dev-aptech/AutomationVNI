using System.Text.RegularExpressions;

namespace Backend.Modules.ContentCrawl;

/// <param name="FeedUrl">Địa chỉ feed tìm được, null nếu chịu.</param>
/// <param name="ItemCount">Số bài đọc thử được — 0 nghĩa là không dùng được.</param>
/// <param name="How">Tìm bằng cách nào, để hiện cho người dùng biết.</param>
/// <param name="Tried">Những địa chỉ đã thử, dùng khi phải báo lỗi.</param>
public sealed record FeedDiscovery(string? FeedUrl, int ItemCount, string How, List<string> Tried);

/// <summary>
/// Tìm feed RSS từ địa chỉ trang báo.
///
/// ═══ VÌ SAO CẦN ═══
///
/// Trước đây người thêm nguồn phải TỰ ĐI TÌM địa chỉ feed. Đó là việc của người biết RSS là
/// gì và biết mở mã nguồn trang — không phải việc của người làm nội dung. Giao cho khách thì
/// họ dán địa chỉ trang chuyên mục vào ô "URL feed", hệ thống đọc HTML bằng bộ đọc XML, và
/// báo một lỗi không ai hiểu.
///
/// Giờ chỉ cần dán địa chỉ trang: https://tuoitre.vn/giao-duc.htm
///
/// ═══ HAI TẦNG, ĐO THẬT TRÊN 6 BÁO ═══
///
///   1. Đọc thẻ khai báo trong HTML          4/6 báo có
///      &lt;link rel="alternate" type="application/rss+xml" href="…"&gt;
///      Đây là cách mọi trình đọc feed vẫn dùng, nên báo nào tử tế đều khai.
///
///   2. Đoán theo mẫu đường dẫn              2/6 còn lại, trúng ngay mẫu ĐẦU TIÊN
///      vnexpress.net/giao-duc     → /rss/giao-duc.rss    60 bài
///      giaoduc.net.vn/            → /rss/home.rss        50 bài
///      tienphong.vn/giao-duc/     → /rss/giao-duc.rss    50 bài
///
/// LUÔN đọc thử feed trước khi trả về. Trả một địa chỉ 200 nhưng là trang HTML thì người dùng
/// lưu nguồn xong mới phát hiện — mà lúc đó lỗi nằm trong nhật ký cào, không ai mở.
/// </summary>
public partial class FeedDiscoveryService(HttpClient http, ILogger<FeedDiscoveryService> logger)
{
    public async Task<FeedDiscovery> DiscoverAsync(string pageUrl, CancellationToken ct = default)
    {
        var tried = new List<string>();
        if (!Uri.TryCreate(pageUrl.Trim(), UriKind.Absolute, out var page))
            return new FeedDiscovery(null, 0, "Địa chỉ không hợp lệ", tried);

        // Người dùng dán thẳng địa chỉ feed cũng phải chạy — đừng bắt họ nhớ mình đang ở ô nào.
        var direct = await CountItemsAsync(page.ToString(), ct);
        if (direct > 0)
            return new FeedDiscovery(page.ToString(), direct, "Địa chỉ bạn nhập đã là feed", tried);
        tried.Add(page.ToString());

        var html = await GetTextAsync(page.ToString(), ct);

        // ── Tầng 1: thẻ khai báo trong HTML ──────────────────────────────────
        if (html is not null)
        {
            foreach (var m in AllFeedLinks(html))
            {
                var href = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
                if (!Uri.TryCreate(page, href, out var abs)) continue;

                // thanhnien.vn khai "https://thanhnien.vn/rss//giao-duc.rss" — hai gạch chéo.
                // Máy chủ của họ vẫn nhận, nhưng chuẩn hoá cho sạch.
                var clean = Regex.Replace(abs.ToString(), @"(?<!:)//+", "/");
                if (tried.Contains(clean)) continue;
                tried.Add(clean);

                var n = await CountItemsAsync(clean, ct);
                if (n > 0) return new FeedDiscovery(clean, n, "Trang có khai báo feed sẵn", tried);
            }
        }

        // ── Tầng 2: đoán theo mẫu ────────────────────────────────────────────
        foreach (var candidate in Candidates(page))
        {
            if (tried.Contains(candidate)) continue;
            tried.Add(candidate);

            var n = await CountItemsAsync(candidate, ct);
            if (n > 0) return new FeedDiscovery(candidate, n, "Đoán theo mẫu đường dẫn", tried);
        }

        logger.LogInformation("Không tìm được feed cho {Url}, đã thử {N} địa chỉ", pageUrl, tried.Count);
        return new FeedDiscovery(null, 0, "Không tìm được feed", tried);
    }

    /// <summary>
    /// Các địa chỉ feed hay gặp ở báo Việt, xếp theo xác suất trúng giảm dần.
    ///
    /// Mẫu /rss/{đường-dẫn}.rss đứng đầu vì nó trúng cả 3 báo đã thử ngay lượt đầu.
    /// </summary>
    private static IEnumerable<string> Candidates(Uri page)
    {
        var root = $"{page.Scheme}://{page.Authority}";
        var stem = Regex.Replace(page.AbsolutePath.TrimEnd('/'), @"\.(htm|html)$", "");

        if (!string.IsNullOrEmpty(stem) && stem != "/")
        {
            yield return $"{root}/rss{stem}.rss";
            yield return $"{root}{stem}.rss";
            yield return $"{root}/rss{stem}";
        }

        yield return $"{root}/rss/home.rss";
        yield return $"{root}/rss.xml";
        yield return $"{root}/feed";
        yield return $"{root}/rss";
    }

    /// <summary>Đếm số bài trong feed. 0 = không phải feed, hoặc feed rỗng — cả hai đều vô dụng.</summary>
    private async Task<int> CountItemsAsync(string url, CancellationToken ct)
    {
        var text = await GetTextAsync(url, ct);
        if (text is null) return 0;

        // Đếm bằng chuỗi chứ không parse XML: feed hỏng một phần vẫn đếm được, và ở đây chỉ
        // cần biết "có phải feed không", việc đọc đúng đã có RssFeedReader lo.
        return ItemTag().Matches(text).Count + EntryTag().Matches(text).Count;
    }

    private async Task<string?> GetTextAsync(string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using var res = await http.GetAsync(url, cts.Token);
            if (!res.IsSuccessStatusCode) return null;

            var bytes = await res.Content.ReadAsByteArrayAsync(cts.Token);
            // Cắt ở 400KB: chỉ cần đủ để nhận ra feed và đếm vài thẻ đầu, không cần cả trang.
            var take = Math.Min(bytes.Length, 400_000);
            return System.Text.Encoding.UTF8.GetString(bytes, 0, take);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || ct.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException) { return null; }
    }

    [GeneratedRegex(@"<link[^>]+type=[""']application/(?:rss|atom)\+xml[""'][^>]*href=[""']([^""']+)[""']",
        RegexOptions.IgnoreCase)]
    private static partial Regex FeedLinkAfter();

    [GeneratedRegex(@"<link[^>]+href=[""']([^""']+)[""'][^>]*type=[""']application/(?:rss|atom)\+xml[""']",
        RegexOptions.IgnoreCase)]
    private static partial Regex FeedLinkBefore();

    /// <summary>
    /// Thứ tự href và type trong thẻ &lt;link&gt; là TUỲ BÁO — có nơi type trước, có nơi href
    /// trước. Chỉ viết một chiều thì mất đúng nửa số báo, mà không có gì báo lỗi: hệ thống rơi
    /// xuống tầng đoán và thường vẫn ra kết quả, chỉ chậm hơn và đôi khi trúng feed khác.
    /// </summary>
    private static IEnumerable<Match> AllFeedLinks(string html)
        => FeedLinkAfter().Matches(html).Concat(FeedLinkBefore().Matches(html));

    [GeneratedRegex(@"<item\b", RegexOptions.IgnoreCase)]
    private static partial Regex ItemTag();

    [GeneratedRegex(@"<entry\b", RegexOptions.IgnoreCase)]
    private static partial Regex EntryTag();
}
