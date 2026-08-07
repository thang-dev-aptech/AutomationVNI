using System.Text;
using System.Text.RegularExpressions;

namespace Backend.Modules.NewsSite;

/// <summary>
/// Đổi văn bản AI trả về sang HTML an toàn, và các tiện ích thoát ký tự dùng chung.
///
/// KHÔNG cho phép thẻ tuỳ ý. Nội dung này do model sinh ra sau khi đọc nguyên văn bài báo —
/// trong đó có thể lẫn thẻ script hoặc thuộc tính onclick từ trang gốc. Ở đây escape sạch
/// TRƯỚC rồi mới tự dựng lại p / h2 / ul / li, nên không có đường nào chèn được mã.
/// </summary>
public static partial class NewsHtml
{
    /// <summary>
    /// Đường dẫn tương đối tới một bài. CÓ đuôi .html, đúng bằng tên file ghi ra đĩa.
    ///
    /// Trước đây sinh "/tin/{slug}" không đuôi cho URL đẹp, nhưng file ghi ra là "{slug}.html"
    /// — chỉ khớp nếu máy chủ được cấu hình try_files $uri $uri.html. Trang chủ vẫn lên bình
    /// thường nên không ai nghi ngờ, chỉ có MỌI link bài là 404. Đã dính thật khi mở bằng
    /// python http.server, và sẽ dính y hệt trên nginx nếu quên dòng try_files.
    ///
    /// Đuôi .html không xấu: dantri.com.vn để .htm cho toàn bộ bài. Đổi lại thì chạy được trên
    /// mọi máy chủ tĩnh mà không phải dặn ai cấu hình gì.
    ///
    /// MỘT nơi sinh đường dẫn cho cả link trong trang, sitemap, canonical, og:url và link dán ở
    /// bình luận Facebook — lệch nhau giữa mấy chỗ đó là lỗi câm, không có gì báo.
    /// </summary>
    public static string ArticlePath(string? slug) => $"/tin/{slug}.html";

    /// <summary>
    /// Thoát TỐI THIỂU: chỉ 5 ký tự có nghĩa trong HTML.
    ///
    /// KHÔNG dùng WebUtility.HtmlEncode vì nó mã hoá cả chữ có dấu thành thực thể số —
    /// "chuyên" ra "chuy&amp;#234;n". Vẫn hiển thị đúng, nhưng trang phình lên, xem mã nguồn
    /// thì không đọc nổi, và thẻ og:title dài gấp rưỡi trong khi Facebook chỉ lấy ~90 ký tự
    /// đầu. Trang khai charset UTF-8 nên chữ Việt để nguyên là đúng chuẩn.
    /// </summary>
    public static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
    }

    /// <summary>
    /// Văn bản thuần → HTML.
    ///
    /// Nhận CẢ HAI cách AI đánh dấu tiêu đề mục, vì đo thật thấy nó dùng markdown dù prompt
    /// chỉ yêu cầu "một dòng tiêu đề ngắn":
    ///   "### Lịch thi và công tác chuẩn bị"   ← dạng model hay trả về
    ///   "Lịch thi và công tác chuẩn bị"       ← dòng ngắn, không dấu chấm cuối
    /// Chỉ nhận một dạng thì nửa số bài ra thân bài không có tiêu đề mục nào.
    /// </summary>
    public static string ToSafeHtml(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return "";

        var sb = new StringBuilder();
        var inList = false;

        foreach (var raw in plain.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("- ") || line.StartsWith("• ") || line.StartsWith("* "))
            {
                if (!inList) { sb.AppendLine("<ul>"); inList = true; }
                sb.AppendLine($"<li>{Esc(StripInlineMarks(line[2..]))}</li>");
                continue;
            }

            if (inList) { sb.AppendLine("</ul>"); inList = false; }

            var heading = MarkdownHeading().Match(line);
            if (heading.Success)
            {
                sb.AppendLine($"<h2>{Esc(StripInlineMarks(heading.Groups[1].Value))}</h2>");
                continue;
            }

            // Dòng ngắn không kết bằng dấu câu = tiêu đề mục.
            if (line.Length <= 70 && !EndsSentence().IsMatch(line))
            {
                sb.AppendLine($"<h2>{Esc(StripInlineMarks(line))}</h2>");
                continue;
            }

            sb.AppendLine($"<p>{Esc(StripInlineMarks(line))}</p>");
        }

        if (inList) sb.AppendLine("</ul>");
        return sb.ToString().Trim();
    }

    /// <summary>
    /// Gỡ dấu markdown còn sót (**đậm**, *nghiêng*, `mã`). Escape xong mà để nguyên thì độc
    /// giả thấy dấu sao lộ ra giữa bài — thà bỏ hẳn còn hơn hiện thô.
    /// </summary>
    private static string StripInlineMarks(string s)
        => InlineMarks().Replace(s, "$1").Trim();

    /// <summary>Đếm từ để ước lượng phút đọc. 200 từ/phút là mức đọc tiếng Việt thường dùng.</summary>
    public static int ReadMinutes(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return 1;
        var words = plain.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Ceiling(words / 200.0));
    }

    /// <summary>
    /// Giờ Việt Nam, không phải UTC. Độc giả thấy "02:30" cho bài đăng lúc 9h30 sáng thì
    /// tưởng trang này của nước ngoài.
    /// </summary>
    public static string VnDate(DateTime utc, bool withTime = false)
    {
        var tz = ResolveVnTimeZone();
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
        return withTime ? local.ToString("dd/MM/yyyy · HH:mm") : local.ToString("dd/MM/yyyy");
    }

    public static string IsoDate(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ");

    /// <summary>
    /// Máy Linux thiếu gói tzdata sẽ ném khi tra "Asia/Ho_Chi_Minh". Rơi về UTC+7 cứng thay vì
    /// để cả bộ sinh trang chết chỉ vì thiếu một gói hệ điều hành.
    /// </summary>
    private static TimeZoneInfo ResolveVnTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "VN", "VN");
    }

    /// <summary>Lượt xem kiểu "2.4K" — cột số trên trang chủ hẹp, số đầy đủ làm vỡ hàng.</summary>
    public static string Views(int n)
        => n >= 1_000_000 ? $"{n / 1_000_000.0:0.#}M"
         : n >= 1_000 ? $"{n / 1_000.0:0.#}K"
         : n.ToString();

    [GeneratedRegex(@"^#{1,4}\s+(.+)$")]
    private static partial Regex MarkdownHeading();

    [GeneratedRegex(@"[.!?:;]$")]
    private static partial Regex EndsSentence();

    [GeneratedRegex(@"[*_`]{1,2}([^*_`]+)[*_`]{1,2}")]
    private static partial Regex InlineMarks();
}
