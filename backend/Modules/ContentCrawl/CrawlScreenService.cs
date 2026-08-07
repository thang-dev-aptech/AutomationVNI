using System.Text.Json;
using Backend.Shared.Ai;
using Microsoft.Extensions.Options;

namespace Backend.Modules.ContentCrawl;

/// <summary>Kết quả chấm một tin.</summary>
public sealed record ScreenVerdict(
    int Score, string Topic, string Reason, string Summary, string CategorySlug);

/// <summary>
/// Chấm điểm tin cào về: có đáng đăng lên fanpage giáo dục của mình không.
///
/// KHÁC hẳn bản nháp AI đã bỏ. Bản nháp viết nguyên một caption rồi vứt — lúc duyệt mỗi page
/// tự sinh nội dung riêng nên không ai dùng tới nó. Bước này chỉ đọc rồi trả về một điểm số
/// và ba dòng mô tả: điểm dùng để LỌC, mô tả dùng để người duyệt liếc trên Telegram là quyết
/// được. Đầu ra ngắn hơn nhiều lần mà lại có người dùng.
///
/// Vì sao lọc từ khoá không đủ: chuyên mục giáo dục của báo có cả câu đố ("Nước nào khai thác
/// khoáng sản nhiều nhất thế giới?") lẫn tin vụ việc ("Rắn dài một mét bò vào lớp mầm non").
/// Bài thứ hai chứa đúng chữ "mầm non" — lọc từ khoá kiểu gì cũng cho qua, chỉ có đọc mới biết
/// nó không phải tin giáo dục mà là tin giật gân.
/// </summary>
public class CrawlScreenService(
    IAiJudgeService ai,
    IOptions<ContentCrawlOptions> options,
    ILogger<CrawlScreenService> logger)
{
    private const string SystemPrompt =
        """
        Bạn là biên tập viên của một trung tâm giáo dục, lọc tin trước khi đăng lên fanpage.
        Đọc bản tin rồi CHẤM ĐIỂM xem có đáng đăng không.

        Chấm CAO (70-100): tin chính sách giáo dục, tuyển sinh, thi cử, học phí, chương trình
        học, hướng nghiệp, du học, kỹ năng nghề — thứ phụ huynh và học sinh cần biết để quyết định.

        Chấm THẤP (0-40) và nói rõ lý do nếu là:
        - Câu đố, trắc nghiệm vui, "bạn có biết", tin giải trí
        - Tin vụ việc/tai nạn/giật gân dù có xảy ra ở trường học
        - Tin thể thao, showbiz, xã hội chung không liên quan việc học
        - Bài quảng cáo trá hình, bài PR cho một đơn vị cụ thể
        - Nội dung bóc bị HỎNG: các đoạn lặp lại nhau, lẫn tít của bài khác, hoặc nội dung
          không hề nói về chuyện mà tiêu đề nêu

        KHÔNG trừ điểm vì bài bị cắt ngắn ở cuối. Bản tin đưa cho bạn có thể đã được lược bớt
        phần đuôi để tiết kiệm, chuyện đó là bình thường — cứ chấm theo phần đọc được.

        CHỈ trả về JSON, không thêm chữ nào ngoài JSON:
        {"score": 0-100, "category": "slug chuyên mục",
         "topic": "chủ đề ngắn gọn 2-4 từ",
         "reason": "một câu ngắn vì sao chấm điểm đó",
         "summary": "2-3 câu tóm tắt tin, nêu đúng dữ kiện trong bài"}
        """;

    /// <summary>
    /// Khối chuyên mục chèn vào prompt. Danh sách ĐÓNG — trước đây trường "topic" để AI tự đặt
    /// tên nên mỗi bài một kiểu, không nhóm được, không dựng nổi trang chuyên mục.
    /// Vẫn giữ "topic" tự do làm nhãn hiển thị phụ, nhưng nó không còn quyết định gì.
    /// </summary>
    private static string CategoryRules =>
        "### CHỌN CHUYÊN MỤC\n"
        + "Trường \"category\" CHỈ được nhận một trong các slug sau:\n"
        + NewsTaxonomy.PromptBlock()
        + "\nTUYỆT ĐỐI KHÔNG đặt slug mới. Không thuộc ba mục đầu thì trả \"" + NewsTaxonomy.OtherSlug + "\".";

    public bool IsAvailable() => options.Value.ScreenEnabled && ai.IsAvailable();

    /// <summary>
    /// Trả về null khi AI hỏng/timeout. Nơi gọi PHẢI hiểu null là "chưa chấm được" và cho tin
    /// đi tiếp vào hàng chờ duyệt — lỗi thì MỞ, không đóng. Lọc nhầm là mất tin trong im lặng;
    /// cho qua nhầm chỉ tốn người duyệt một cú bấm Bỏ.
    /// </summary>
    public async Task<ScreenVerdict?> ScreenAsync(CrawledArticleModel article, CancellationToken ct = default)
    {
        if (!IsAvailable()) return null;

        // Cắt ở RANH GIỚI ĐOẠN chứ không cắt giữa câu, và nói rõ là đã lược bớt.
        // Đo thật: cắt cứng ở 3000 ký tự làm bài "Thủ tướng: Cắt giảm các kỳ thi" (3130 ký
        // tự) đứt ngang chữ "nế", AI đọc thấy cụt nên chấm 35 kèm lý do "bị cắt cụt giữa
        // câu" — tin chính sách đúng thứ cần đăng lại bị lọc, bởi chính khâu chuẩn bị dữ liệu.
        var body = TrimForScreening(article.Content ?? "", 6000);

        var user =
            $"""
             TIÊU ĐỀ: {article.Title}
             TÓM TẮT: {article.Summary}

             NỘI DUNG:
             {body}
             """;

        var raw = await ai.AskAsync(
            SystemPrompt + "\n\n" + CategoryRules, user, maxTokens: 700, temperature: 0,
            timeout: TimeSpan.FromSeconds(options.Value.ScreenTimeoutSeconds), ct: ct);

        if (string.IsNullOrWhiteSpace(raw))
        {
            logger.LogDebug("Chấm điểm tin {Id} không có phản hồi", article.Id);
            return null;
        }

        return Parse(raw, article.Id);
    }

    /// <summary>Cắt ở ranh giới đoạn gần nhất, và ghi rõ đã lược để AI khỏi tưởng bài hỏng.</summary>
    private static string TrimForScreening(string body, int max)
    {
        body = body.Trim();
        if (body.Length <= max) return body;

        var cut = body.LastIndexOf("\n\n", max, StringComparison.Ordinal);
        if (cut < max / 2) cut = body.LastIndexOf(". ", max, StringComparison.Ordinal) + 1;
        if (cut < max / 2) cut = max;

        return body[..cut].TrimEnd() + "\n\n[Phần sau của bài đã được lược bớt để chấm điểm.]";
    }

    private ScreenVerdict? Parse(string raw, Guid articleId)
    {
        // Model hay bọc JSON trong ```json … ``` dù đã dặn. Cắt lấy khối {...} ngoài cùng
        // thay vì bắt nó tuân thủ tuyệt đối — rẻ hơn nhiều so với một lượt gọi lại.
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var root = doc.RootElement;

            var score = root.TryGetProperty("score", out var s) && s.TryGetInt32(out var v)
                ? Math.Clamp(v, 0, 100)
                : -1;
            if (score < 0) return null;

            // Slug lạ thì về "khac" nhưng PHẢI log — im lặng nuốt là cách "topic" biến thành
            // chuỗi tự do như trước đây mà không ai nhận ra.
            var rawSlug = Str(root, "category");
            var cat = NewsTaxonomy.Resolve(rawSlug);
            if (!string.IsNullOrWhiteSpace(rawSlug) && cat.Slug != rawSlug.Trim().ToLowerInvariant())
                logger.LogWarning(
                    "AI trả chuyên mục lạ '{Raw}' cho tin {Id}, xếp vào {Slug}", rawSlug, articleId, cat.Slug);

            return new ScreenVerdict(
                score,
                Str(root, "topic"),
                Str(root, "reason"),
                Str(root, "summary"),
                cat.Slug);
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "Không đọc được JSON chấm điểm tin {Id}", articleId);
            return null;
        }
    }

    private static string Str(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? (el.GetString() ?? "").Trim()
            : "";
}
