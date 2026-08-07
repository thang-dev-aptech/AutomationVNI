using System.Net;
using System.Text.RegularExpressions;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Bóc tiêu đề / tóm tắt / ảnh / thân bài từ HTML thô — KHÔNG cần trình duyệt.
///
/// Vì sao làm được: báo Việt Nam render sẵn toàn bộ bài ở server. Đã đo trên 12 bài của 6 báo
/// (dantri, vnexpress, thanhnien, tuoitre, vietnamnet, giaoduc.net.vn): tải bằng HttpClient
/// thuần rồi bóc thẻ, lấy được 100–105% số ký tự so với OpenClaw, mất 0,021 giây mỗi bài thay
/// vì 41 giây. Giả định "phải render JavaScript" từng khiến dự án chọn trình duyệt là SAI với
/// nhóm trang này.
///
/// Vẫn có ~6% bài không bóc được (bài dạng ảnh, Dmagazine) — nơi gọi phải rơi sang OpenClaw,
/// xem <see cref="HttpArticleFetcher"/>.
/// </summary>
public static partial class HtmlArticleParser
{
    /// <summary>
    /// Đoạn ngắn hơn mức này coi như không phải thân bài mà là chú thích ảnh, nhãn, hoặc dòng
    /// điều hướng. 60 ký tự đo trên 12 bài thật: hạ xuống 40 thì lọt chú thích ảnh, nâng lên
    /// 80 thì mất câu chốt của nhiều đoạn.
    /// </summary>
    private const int MinBlockLength = 60;

    public static string? ExtractTitle(string html)
        => Meta(html, "og:title") ?? FirstGroup(H1Tag(), html) ?? FirstGroup(TitleTag(), html);

    public static string? ExtractSummary(string html)
        => Meta(html, "og:description") ?? MetaName(html, "description");

    public static string? ExtractImage(string html) => Meta(html, "og:image");

    public static string? ExtractAuthor(string html)
        => MetaName(html, "author") ?? Meta(html, "article:author");

    public static DateTime? ExtractPublishedUtc(string html)
    {
        foreach (var raw in new[]
                 {
                     Meta(html, "article:published_time"),
                     MetaName(html, "pubdate"),
                     Meta(html, "og:updated_time"),
                 })
        {
            if (!string.IsNullOrWhiteSpace(raw)
                && DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var dto))
                return dto.UtcDateTime;
        }
        return null;
    }

    /// <summary>
    /// Bóc thân bài.
    ///
    /// Lấy CẢ BA loại khối chứ không chỉ &lt;p&gt;: đo thật cho thấy chỉ lấy &lt;p&gt; thì
    /// vnexpress ra 1.830 ký tự còn lấy thêm div.Normal/&lt;article&gt; ra 4.242 — hụt hơn một
    /// nửa. VnExpress đặt nhiều đoạn trong div.Normal chứ không phải thẻ p.
    /// </summary>
    public static string ExtractBody(string html)
    {
        var doc = StripNoise(html);

        var blocks = new List<string>();
        foreach (var m in PTag().Matches(doc).Cast<Match>()) blocks.Add(m.Groups[1].Value);
        foreach (var m in NormalDiv().Matches(doc).Cast<Match>()) blocks.Add(m.Groups[1].Value);
        foreach (var m in ArticleTag().Matches(doc).Cast<Match>()) blocks.Add(m.Groups[1].Value);

        var seen = new HashSet<string>();
        var kept = new List<string>();

        foreach (var raw in blocks)
        {
            var text = Clean(raw);
            if (text.Length < MinBlockLength) continue;
            if (JunkLine().IsMatch(text)) continue;

            // Khử trùng theo 60 ký tự đầu: <article> bọc cả các <p> đã lấy ở trên nên cùng một
            // đoạn xuất hiện hai lần. Không khử thì thân bài dài gấp đôi và AI đọc thấy lặp,
            // chấm điểm thấp vì tưởng "nội dung bóc hỏng".
            var key = text[..Math.Min(60, text.Length)];
            if (!seen.Add(key)) continue;

            kept.Add(text);
        }

        return string.Join("\n\n", kept);
    }

    private static string StripNoise(string html)
    {
        foreach (var tag in new[] { "script", "style", "noscript", "iframe", "svg", "form" })
            html = Regex.Replace(html, $"<{tag}\\b.*?</{tag}>", " ",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return html;
    }

    private static string Clean(string fragment)
    {
        var text = TagStrip().Replace(fragment, " ");
        text = WebUtility.HtmlDecode(text);
        return Spaces().Replace(text, " ").Trim();
    }

    private static string? Meta(string html, string property)
        => FirstGroup(new Regex(
            $"<meta[^>]+property=[\"']{Regex.Escape(property)}[\"'][^>]+content=[\"']([^\"']*)[\"']",
            RegexOptions.IgnoreCase), html)
           ?? FirstGroup(new Regex(
               $"<meta[^>]+content=[\"']([^\"']*)[\"'][^>]+property=[\"']{Regex.Escape(property)}[\"']",
               RegexOptions.IgnoreCase), html);

    private static string? MetaName(string html, string name)
        => FirstGroup(new Regex(
            $"<meta[^>]+name=[\"']{Regex.Escape(name)}[\"'][^>]+content=[\"']([^\"']*)[\"']",
            RegexOptions.IgnoreCase), html);

    private static string? FirstGroup(Regex re, string input)
    {
        var m = re.Match(input);
        if (!m.Success) return null;
        var v = WebUtility.HtmlDecode(TagStrip().Replace(m.Groups[1].Value, " ")).Trim();
        return string.IsNullOrWhiteSpace(v) ? null : Spaces().Replace(v, " ");
    }

    [GeneratedRegex(@"<p\b[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex PTag();

    // VnExpress dùng div.Normal cho phần lớn đoạn văn — bỏ qua là hụt hơn một nửa bài.
    [GeneratedRegex(@"<div[^>]*class=""[^""]*\bNormal\b[^""]*""[^>]*>(.*?)</div>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex NormalDiv();

    [GeneratedRegex(@"<article\b[^>]*>(.*?)</article>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ArticleTag();

    [GeneratedRegex(@"<h1\b[^>]*>(.*?)</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex H1Tag();

    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagStrip();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(Ảnh|Nguồn|Xem thêm|Video|Đọc thêm|Theo)\s*[:：]",
        RegexOptions.IgnoreCase)]
    private static partial Regex JunkLine();
}
