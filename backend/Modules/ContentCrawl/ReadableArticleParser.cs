namespace Backend.Modules.ContentCrawl;

/// <summary>Kết quả bóc một trang báo.</summary>
public sealed record ParsedArticle(
    string Body, string? Title, string? Summary, string? Author, string? ImageUrl, DateTime? PublishedUtc);

/// <summary>
/// Bóc thân bài bằng SmartReader — bản cổng .NET của Mozilla Readability, thứ Firefox dùng
/// cho chế độ Đọc.
///
/// ═══ VÌ SAO THAY BỘ TỰ VIẾT ═══
///
/// HtmlArticleParser gom &lt;p&gt; + div.Normal + &lt;article&gt; bằng biểu thức chính quy. Nó chạy được
/// với 6 báo đã thử, nhưng gãy theo cách khó thấy: đo trên 15 tin bị chấm rớt, 4 tin (27%)
/// KHÔNG phải tin rác mà là LỖI BÓC — ví dụ "328 thí sinh Tuyên Quang phải thi lại" bị chấm
/// 15 điểm kèm lý do "nội dung chỉ chứa thông báo giao diện". Tin thật, mất vì bộ bóc hụt.
///
/// SmartReader chấm điểm từng khối theo mật độ chữ, tỉ lệ link, số dấu phẩy — cùng thuật toán
/// Firefox dùng — nên không phụ thuộc tên class của từng báo. Nó cũng trả sẵn ảnh đại diện,
/// tác giả và ngày đăng, đúng những thứ đang phải bóc riêng bằng thẻ og.
///
/// ═══ VÌ SAO GIỮ BỘ CŨ LÀM ĐƯỜNG LUI ═══
///
/// Readability tối ưu cho báo phương Tây. Chưa có số đo trên báo Việt ở quy mô lớn, nên bộ
/// mới ra ngắn hơn ngưỡng thì thử lại bằng bộ cũ và LẤY BẢN DÀI HƠN. Đổi thẳng mà không có
/// đường lui là đánh cược cả luồng cào vào một thư viện chưa kiểm.
///
/// Ghi log khi hai bộ chênh nhau nhiều — đó là dữ liệu để quyết định có bỏ hẳn bộ cũ không.
/// </summary>
public class ReadableArticleParser(ILogger<ReadableArticleParser> logger)
{
    /// <summary>
    /// Dưới ngưỡng này coi như bóc hụt và phải thử bộ cũ.
    ///
    /// 500 chứ không phải 2.000: đây là ngưỡng "có bóc được gì không", khác hẳn ngưỡng chất
    /// lượng ở IngestAsync. Bài ngắn thật vẫn phải qua được đây rồi mới bị lọc ở đó, nếu
    /// không thì hai ngưỡng cùng lái một quyết định và không ai biết cái nào đã chặn.
    /// </summary>
    private const int UsableLength = 500;

    public ParsedArticle Parse(string html, string url)
    {
        var fallback = HtmlArticleParser.ExtractBody(html);

        try
        {
            // KHÔNG dùng Reader.ParseArticle(url, html) — nó chạy với ngưỡng mặc định và
            // TỪ CHỐI gần như mọi bài báo Việt.
            //
            // Đo thật: 5/5 bài trả về Completed=false, TextContent rỗng. Thủ phạm là
            // CharThreshold mặc định 500 — tin "điểm chuẩn Trường Đại học Y Dược Hải Phòng"
            // chỉ có 1.021 ký tự thân bài, và Readability yêu cầu vượt ngưỡng ở BƯỚC DÒ nên
            // bài ngắn bị loại trước khi kịp bóc.
            //
            // Hạ CharThreshold xuống 100 và bỏ ngưỡng điểm: việc quyết định bài dài bao nhiêu
            // là đủ đã có MinContentLength ở tầng trên lo. Để hai nơi cùng chặn theo độ dài
            // thì không ai biết cái nào đã loại bài.
            var article = new SmartReader.Reader(url, html)
            {
                CharThreshold = 100,
                MinScoreReaderable = 0,
                ContinueIfNotReadable = true,
            }.GetArticle();

            var body = (article.TextContent ?? "").Trim();

            // LẤY BẢN DÀI HƠN khi bản mới hụt. Không phải "bản nào cũng được": bộ cũ đôi khi
            // vơ cả menu và chân trang, nên chỉ dùng nó khi bản mới rõ ràng thiếu.
            if (body.Length < UsableLength && fallback.Length > body.Length)
            {
                logger.LogInformation(
                    "SmartReader chỉ bóc được {New} ký tự ở {Url}, dùng bộ cũ ({Old} ký tự)",
                    body.Length, url, fallback.Length);
                return WithFallbackMeta(fallback, html, article);
            }

            // Chênh lệch lớn theo chiều ngược lại cũng đáng ghi: có thể bộ cũ đang vơ rác.
            if (fallback.Length > body.Length * 2 && body.Length >= UsableLength)
                logger.LogDebug(
                    "Bộ cũ dài gấp {Ratio:F1} lần SmartReader ở {Url} — nhiều khả năng vơ cả menu",
                    (double)fallback.Length / Math.Max(1, body.Length), url);

            return new ParsedArticle(
                body,
                Trim(article.Title),
                Trim(article.Excerpt) ?? HtmlArticleParser.ExtractSummary(html),
                Trim(article.Author) ?? HtmlArticleParser.ExtractAuthor(html),
                Trim(article.FeaturedImage) ?? HtmlArticleParser.ExtractImage(html),
                article.PublicationDate?.ToUniversalTime() ?? HtmlArticleParser.ExtractPublishedUtc(html));
        }
        catch (Exception ex)
        {
            // SmartReader hỏng KHÔNG được làm mất bài. Rơi về bộ cũ và đi tiếp.
            logger.LogWarning(ex, "SmartReader lỗi ở {Url} — dùng bộ cũ", url);
            return WithFallbackMeta(fallback, html, null);
        }
    }

    private static ParsedArticle WithFallbackMeta(string body, string html, SmartReader.Article? a)
        => new(
            body,
            Trim(a?.Title) ?? HtmlArticleParser.ExtractTitle(html),
            HtmlArticleParser.ExtractSummary(html),
            HtmlArticleParser.ExtractAuthor(html),
            Trim(a?.FeaturedImage) ?? HtmlArticleParser.ExtractImage(html),
            HtmlArticleParser.ExtractPublishedUtc(html));

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
