using System.Text;
using System.Text.Json;
using Backend.Modules.ContentCrawl;
using Backend.Shared.Ai;
using Backend.Shared.Text;
using Microsoft.Extensions.Options;

namespace Backend.Modules.NewsSite;

public sealed record TimelineEntry(string Time, string What);

public sealed record ComposedArticle(
    string Title,
    string Sapo,
    string BodyPlain,
    List<string> KeyPoints,
    List<TimelineEntry> Timeline,
    /// <summary>Dữ kiện AI tự thêm, không có trong tư liệu. Rỗng thì mới được xuất bản.</summary>
    List<string> FactWarnings);

/// <summary>
/// AI viết BÀI HOÀN CHỈNH cho website từ tư liệu gốc.
///
/// Đây là khâu chưa từng có: hệ thống trước chỉ biết sinh caption Facebook ≤130 ký tự.
/// Một lượt gọi cho MỖI TIN — không nhân theo số page. Caption Facebook sau này rút từ
/// keyPoints của chính bài này, nên chi phí không còn nhân lên:
///     trước: 12 page × 9.000 token = 108.000
///     sau:   4.000 (viết bài) + 12 × 800 (rút caption) = 13.600
///
/// Bốn ràng buộc trong prompt đều có lý do đã trả giá:
///   - Chỉ dùng dữ kiện có trong tư liệu. Prompt mặc định của hệ thống đòi "80–160 từ" mà
///     tư liệu đôi khi chỉ vài trăm chữ — đưa vào thế nào cũng phải bịa cho đủ.
///   - Không sao chép quá 1 câu liên tiếp. Đây là ranh giới pháp lý, không phải văn phong.
///   - Không hashtag, không CTA bán hàng. Bản tin lẫn giọng quảng cáo là người đọc nhận ra ngay.
///   - Bài không xác định được nguồn thì KHÔNG xuất bản — chặn ở nơi gọi, không chỉ dặn prompt.
/// </summary>
public class NewsComposeService(
    IAiJudgeService ai,
    IOptions<NewsSiteOptions> options,
    ILogger<NewsComposeService> logger)
{
    /// <summary>
    /// Toàn văn đưa vào prompt. 5.000 ký tự ≈ 3.600 token — đủ dày để viết đúng dữ kiện mà
    /// không đốt cửa sổ ngữ cảnh. Cắt ở ranh giới đoạn, xem lý do ở TrimForPrompt.
    /// </summary>
    private const int MaxSourceChars = 5000;

    private static string SystemPrompt =>
        $$"""
        Bạn là biên tập viên của trang tin giáo dục VNI Education. Viết lại tư liệu dưới đây
        thành MỘT BÀI HOÀN CHỈNH cho website.

        ### RÀNG BUỘC BẮT BUỘC (ưu tiên cao nhất)
        - CHỈ dùng dữ kiện CÓ trong tư liệu. TUYỆT ĐỐI KHÔNG thêm con số, tỉ lệ, mốc thời gian,
          tên người, tên trường, tên cơ quan hay phát ngôn nào không xuất hiện ở tư liệu.
        - KHÔNG sao chép nguyên văn quá MỘT câu liên tiếp. Viết lại bằng lời của mình,
          đổi cả cấu trúc câu chứ không chỉ thay vài từ. Đoạn nào thấy mình đang chép thì
          dừng lại, tóm ý rồi diễn đạt kiểu khác.
        - TUYỆT ĐỐI KHÔNG viết một mốc thời gian nào không có trong tư liệu. Không suy ra,
          không tính thêm, không "ước chừng". Tư liệu không nói thì bỏ mốc đó.
        - KHÔNG hashtag. KHÔNG mời chào bán hàng, không nhắc khoá học, không hotline.
        - Giọng đưa tin khách quan, bình tĩnh. Không dùng emoji.
        - Tư liệu có thể đã bị lược bớt phần đuôi — chuyện bình thường, KHÔNG trừ điểm hay
          nhắc tới việc đó trong bài.

        ### CẤU TRÚC BÀI
        - title: tít bài 50–70 ký tự, cụ thể, không hô hào, không dấu chấm cuối câu.
        - sapo: 2–3 câu (150–200 ký tự) nêu đúng cốt lõi. Đây là dòng hiện trên thẻ ngoài
          trang chủ và khi chia sẻ lên Facebook.
        - body: {{MinWords}}–{{MaxWords}} TỪ, chia 3–4 mục. Mỗi mục bắt đầu bằng MỘT DÒNG
          TIÊU ĐỀ NGẮN (không quá 60 ký tự, không có dấu chấm cuối), rồi tới 2–3 đoạn văn.
          Gạch đầu dòng thì bắt đầu bằng "- ".
        - keyPoints: 3–5 ý ngắn "điều cần nhớ", mỗi ý một câu.
        - timeline: các mốc thời gian CÓ TRONG BÀI, dạng [{"time":"09/08 · 17:00","what":"..."}].
          Bài không có mốc nào thì trả mảng RỖNG — đừng bịa mốc cho đủ.

        CHỈ trả về JSON, không rào ```, không thêm chữ nào ngoài JSON:
        {"title":"...","sapo":"...","body":"...","keyPoints":["..."],"timeline":[]}
        """;

    private static int MinWords => 500;
    private static int MaxWords => 800;

    public bool IsAvailable() => options.Value.ComposeEnabled && ai.IsAvailable();

    /// <summary>
    /// Trả null khi AI hỏng hoặc trả JSON không đọc được. Nơi gọi PHẢI hiểu null là "chưa
    /// viết được" và cho thử lại, KHÔNG được xuất bản bài rỗng.
    /// </summary>
    public async Task<ComposedArticle?> ComposeAsync(
        CrawledArticleModel article, CancellationToken ct = default)
    {
        if (!IsAvailable()) return null;

        var material = BuildMaterial(article);
        if (material.Length < 200)
        {
            logger.LogWarning(
                "Tư liệu cho tin {Id} chỉ {Len} ký tự — quá mỏng để viết bài", article.Id, material.Length);
            return null;
        }

        var raw = await ai.AskAsync(
            SystemPrompt, material,
            // Bài 800 từ tiếng Việt ≈ 2.400 token, cộng JSON bọc ngoài. 4.000 là rộng rãi;
            // để chật thì JSON đứt giữa chừng và Parse trả null, tốn cả lượt gọi.
            maxTokens: 4000,
            temperature: 0.4,
            timeout: TimeSpan.FromSeconds(options.Value.ComposeTimeoutSeconds),
            ct: ct);

        if (string.IsNullOrWhiteSpace(raw))
        {
            logger.LogWarning("AI không trả lời khi viết bài cho tin {Id}", article.Id);
            return null;
        }

        var composed = Parse(raw, article.Id);
        if (composed is null) return null;

        // ── Soi dữ kiện bịa ────────────────────────────────────────────────────
        // Ràng buộc trong prompt GIẢM bịa chứ không triệt tiêu. Đo thật trên bài đầu tiên
        // sinh ra: AI bịa bốn mốc "10.8, 20.8, 21.8, 28.8" không có ở bất kỳ đâu trong toàn
        // văn 8.676 ký tự. Một mốc sai trên trang tin trường học là phụ huynh lỡ hạn thật,
        // nên đây là chốt chặn tất định, không phụ thuộc model.
        var everything = string.Join("\n", new[]
        {
            composed.Title, composed.Sapo, composed.BodyPlain,
            string.Join("\n", composed.KeyPoints),
            string.Join("\n", composed.Timeline.Select(t => $"{t.Time} {t.What}")),
        });

        var warnings = CrawlContentGuard.FindUnsupportedFacts(
            everything,
            // Soi với TOÀN VĂN chứ không phải phần đã cắt đưa vào prompt — nếu không thì mọi
            // dữ kiện nằm ở phần đuôi bị lược đều bị báo nhầm là bịa.
            $"{article.Title}\n{article.Summary}\n{article.Content}");

        if (warnings.Count > 0)
            logger.LogWarning(
                "Bài cho tin {Id} có {N} dữ kiện không có trong tư liệu: {W}",
                article.Id, warnings.Count, string.Join(" · ", warnings.Take(4)));

        return composed with { FactWarnings = warnings };
    }

    private static string BuildMaterial(CrawledArticleModel a)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## TƯ LIỆU GỐC (CHỈ được dùng dữ kiện trong đây)");
        sb.AppendLine($"Tiêu đề gốc: {a.Title.Trim()}");
        if (!string.IsNullOrWhiteSpace(a.Summary)) sb.AppendLine($"Tóm tắt: {a.Summary.Trim()}");
        if (a.PublishedAt.HasValue) sb.AppendLine($"Ngày đăng gốc: {a.PublishedAt.Value:dd/MM/yyyy}");
        sb.AppendLine();
        sb.AppendLine("### NỘI DUNG");
        sb.AppendLine(TrimForPrompt(a.Content ?? a.Summary ?? "", MaxSourceChars));
        return sb.ToString();
    }

    /// <summary>
    /// Cắt ở ranh giới ĐOẠN, và nói rõ đã lược bớt.
    ///
    /// Đã trả giá một lần ở bước chấm điểm: cắt cứng giữa câu làm AI tưởng bài hỏng rồi chấm
    /// 35 điểm cho một tin chính sách tốt. Ở đây hậu quả còn nặng hơn — nó sẽ viết một bài
    /// cụt đuôi rồi đăng thẳng lên website.
    /// </summary>
    private static string TrimForPrompt(string body, int max)
    {
        body = body.Trim();
        if (body.Length <= max) return body;

        var cut = body.LastIndexOf("\n\n", max, StringComparison.Ordinal);
        if (cut < max / 2) cut = body.LastIndexOf(". ", max, StringComparison.Ordinal) + 1;
        if (cut < max / 2) cut = max;

        return body[..cut].TrimEnd() + "\n\n[Phần sau của bài gốc đã được lược bớt.]";
    }

    private ComposedArticle? Parse(string raw, Guid articleId)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            logger.LogWarning("Bài viết cho tin {Id} không có JSON", articleId);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var root = doc.RootElement;

            var title = Str(root, "title");
            var body = Str(root, "body");

            // Thiếu tít hoặc thân bài thì bài vô dụng — trả null để thử lại, đừng lưu một bản
            // rỗng rồi đẩy lên web.
            if (string.IsNullOrWhiteSpace(title) || body.Length < 300)
            {
                logger.LogWarning(
                    "Bài viết cho tin {Id} thiếu nội dung (tít {T} ký tự, thân {B} ký tự)",
                    articleId, title.Length, body.Length);
                return null;
            }

            return new ComposedArticle(
                title, Str(root, "sapo"), body,
                StrList(root, "keyPoints"),
                TimelineList(root),
                []);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "JSON bài viết cho tin {Id} hỏng", articleId);
            return null;
        }
    }

    private static string Str(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? (el.GetString() ?? "").Trim()
            : "";

    private static List<string> StrList(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return [];
        return el.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => (x.GetString() ?? "").Trim())
            .Where(x => x.Length > 0)
            .ToList();
    }

    private static List<TimelineEntry> TimelineList(JsonElement root)
    {
        if (!root.TryGetProperty("timeline", out var el) || el.ValueKind != JsonValueKind.Array) return [];

        var list = new List<TimelineEntry>();
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var time = Str(item, "time");
            var what = Str(item, "what");
            if (time.Length > 0 && what.Length > 0) list.Add(new TimelineEntry(time, what));
        }
        return list;
    }

    /// <summary>
    /// Số phút đọc tính từ số từ, KHÔNG hỏi AI. Hỏi thì nó đoán và ra số lẻ vô lý.
    /// 200 từ/phút là mức đọc tiếng Việt thường dùng.
    /// </summary>
    public static int EstimateReadMinutes(string plain)
        => Math.Max(1, (int)Math.Ceiling(VietnameseTextHelper.TokenList(plain).Count / 200.0));
}
