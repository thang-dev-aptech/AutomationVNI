using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Backend.Shared.Text;

namespace Backend.Modules.ContentCrawl;

public sealed record RssItem(
    string Title,
    string? Summary,
    string Link,
    string? Guid,
    string? Author,
    string? Category,
    string? ThumbnailUrl,
    DateTime? PublishedAtUtc);

/// <summary>
/// Đọc feed RSS 2.0 / Atom. Bốn chi tiết dưới đây đều đo trên feed thật của báo Việt Nam,
/// làm sai cái nào là hỏng im lặng:
///
/// 1. giaoduc.net.vn LUÔN trả gzip kể cả khi client không xin. Mặc định .NET là
///    DecompressionMethods.None nên đọc ra binary rác. Handler đã bật giải nén, ở đây
///    kiểm thêm magic byte 1F 8B phòng proxy nào đó gỡ mất header Content-Encoding.
/// 2. dantri.com.vn có BOM UTF-8 đầu file. XDocument.Parse(string) ném lỗi vì BOM,
///    XDocument.Load(Stream) thì không — nên TUYỆT ĐỐI đọc bằng stream.
/// 3. &lt;description&gt; là CDATA chứa HTML HỎNG (&lt;a/&gt;&lt;img/&gt;&lt;/a&gt;&lt;/br&gt;), không parse
///    bằng XML được. Thumbnail lấy bằng regex, tóm tắt lấy bằng gỡ thẻ.
/// 4. pubDate dạng "Wed, 05 Aug 2026 08:14:26 +0700" — DateTimeOffset.TryParse với
///    InvariantCulture đọc được. Parse hỏng thì để null và BỎ bộ lọc lookback cho bài đó,
///    đừng vứt bài chỉ vì cái ngày khó đọc.
/// </summary>
public partial class RssFeedReader(HttpClient http, ILogger<RssFeedReader> logger)
{
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace MediaNs = "http://search.yahoo.com/mrss/";
    private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";

    private const int MaxSummaryLength = 1000;

    public async Task<List<RssItem>> FetchAsync(string url, int maxItems, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length == 0) return [];

        await using var stream = OpenPossiblyGzipped(bytes);
        XDocument doc;
        try
        {
            doc = XDocument.Load(stream);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidOperationException(
                $"Feed không phải XML hợp lệ ({ex.Message}). Kiểm tra URL có trả về trang HTML thay vì RSS không.", ex);
        }

        var items = ParseRss(doc, maxItems);
        if (items.Count == 0) items = ParseAtom(doc, maxItems);

        logger.LogInformation("RSS {Url} → {Count} item", url, items.Count);
        return items;
    }

    /// <summary>Bọc GZipStream nếu thấy magic byte, ngược lại trả stream thường.</summary>
    private static Stream OpenPossiblyGzipped(byte[] bytes)
    {
        var raw = new MemoryStream(bytes, writable: false);
        if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            return new GZipStream(raw, CompressionMode.Decompress);
        return raw;
    }

    private static List<RssItem> ParseRss(XDocument doc, int maxItems)
    {
        var result = new List<RssItem>();
        foreach (var item in doc.Descendants("item"))
        {
            var title = Clean(item.Element("title")?.Value);
            var link = Clean(item.Element("link")?.Value);
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) continue;

            var rawDescription = item.Element("description")?.Value;
            result.Add(new RssItem(
                Title: title,
                Summary: BuildSummary(rawDescription, item),
                Link: link,
                Guid: Clean(item.Element("guid")?.Value) ?? link,
                Author: Clean(item.Element(DcNs + "creator")?.Value) ?? Clean(item.Element("author")?.Value),
                Category: Clean(item.Element("category")?.Value),
                ThumbnailUrl: ExtractThumbnail(item, rawDescription),
                PublishedAtUtc: ParseDate(item.Element("pubDate")?.Value)));

            if (result.Count >= maxItems) break;
        }
        return result;
    }

    private static List<RssItem> ParseAtom(XDocument doc, int maxItems)
    {
        var result = new List<RssItem>();
        foreach (var entry in doc.Descendants(AtomNs + "entry"))
        {
            var title = Clean(entry.Element(AtomNs + "title")?.Value);
            var link = entry.Elements(AtomNs + "link")
                .Select(l => (string?)l.Attribute("href"))
                .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(link)) continue;

            var rawDescription = entry.Element(AtomNs + "summary")?.Value
                                 ?? entry.Element(AtomNs + "content")?.Value;
            result.Add(new RssItem(
                Title: title,
                Summary: BuildSummary(rawDescription, entry),
                Link: link,
                Guid: Clean(entry.Element(AtomNs + "id")?.Value) ?? link,
                Author: Clean(entry.Element(AtomNs + "author")?.Element(AtomNs + "name")?.Value),
                Category: (string?)entry.Element(AtomNs + "category")?.Attribute("term"),
                ThumbnailUrl: ExtractThumbnail(entry, rawDescription),
                PublishedAtUtc: ParseDate(entry.Element(AtomNs + "updated")?.Value
                                          ?? entry.Element(AtomNs + "published")?.Value)));

            if (result.Count >= maxItems) break;
        }
        return result;
    }

    private static string? BuildSummary(string? rawDescription, XElement item)
    {
        var text = VietnameseTextHelper.StripHtml(rawDescription);
        if (string.IsNullOrWhiteSpace(text))
            text = VietnameseTextHelper.StripHtml(item.Element("summary")?.Value);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Length > MaxSummaryLength ? text[..MaxSummaryLength] : text;
    }

    /// <summary>Ưu tiên thẻ chuẩn, cuối cùng mới bới regex trong CDATA description.</summary>
    private static string? ExtractThumbnail(XElement item, string? rawDescription)
    {
        var fromMedia = (string?)item.Element(MediaNs + "content")?.Attribute("url")
                        ?? (string?)item.Element(MediaNs + "thumbnail")?.Attribute("url")
                        ?? (string?)item.Element("enclosure")?.Attribute("url");
        if (!string.IsNullOrWhiteSpace(fromMedia)) return fromMedia.Trim();

        if (string.IsNullOrWhiteSpace(rawDescription)) return null;
        var match = ImgSrcPattern().Match(rawDescription);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTimeOffset.TryParse(
            raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto)
            ? dto.UtcDateTime
            : null;
    }

    /// <summary>
    /// Chuẩn hoá tiêu đề/tác giả. PHẢI giải mã entity: thanhnien.vn trả tiêu đề dạng
    /// "Vụ ti&amp;ecirc;u cực thi tốt nghiệp" — để nguyên thì tokenize ra rác và điểm
    /// tương đồng tính sai, mà không có lỗi nào báo ra.
    /// </summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var decoded = WebUtility.HtmlDecode(value.Trim());
        return WhitespacePattern().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex("""<img[^>]+src=['"]([^'"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ImgSrcPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
