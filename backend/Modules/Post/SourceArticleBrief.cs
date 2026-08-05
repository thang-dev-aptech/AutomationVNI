using System.Text;
using System.Text.Json;

namespace Backend.Modules.Post;

/// <summary>
/// Tư liệu gốc của một tin đã cào, đi kèm Post để AI viết đúng dữ kiện.
/// <paramref name="Content"/> là TOÀN VĂN bài báo (có thể null nếu bóc hỏng).
/// </summary>
public sealed record SourceArticleBrief(
    string Title, string? Summary, string? SourceName, string? SourceUrl, DateTime? PublishedAt,
    string? Content = null);

/// <summary>
/// Đọc/ghi khối sourceArticle trong Post.ExtraJson (tiền lệ: pendingSchedule của bulk-import).
///
/// Vì sao đi qua ExtraJson mà không thêm cột: fan-out tạo N post cho N kênh, mỗi post tự
/// sinh nội dung riêng theo PageContext của kênh đó. Tư liệu gốc chỉ cần đi kèm để pipeline
/// đọc lại lúc dựng prompt — không truy vấn, không lọc, không thống kê theo nó.
///
/// An toàn vì MergeTextGenerationExtraJson (GenerationJobPipelineService) deserialize ra
/// dictionary rồi CHỈ set khoá textGeneration, nên sourceArticle và pendingSchedule sống sót
/// qua bước sinh nội dung. Code nào sau này GHI ĐÈ ExtraJson thay vì merge sẽ phá cả lịch
/// đăng lẫn tư liệu, trong im lặng.
/// </summary>
public static class SourceArticleHelper
{
    public const string ExtraJsonKey = "sourceArticle";

    public static SourceArticleBrief? TryRead(string? extraJson)
    {
        if (string.IsNullOrWhiteSpace(extraJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(extraJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            JsonElement block = default;
            var found = false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!prop.Name.Equals(ExtraJsonKey, StringComparison.OrdinalIgnoreCase)) continue;
                block = prop.Value;
                found = true;
                break;
            }
            if (!found || block.ValueKind != JsonValueKind.Object) return null;

            string? Read(string name)
            {
                foreach (var prop in block.EnumerateObject())
                    if (prop.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                        && prop.Value.ValueKind == JsonValueKind.String)
                        return prop.Value.GetString();
                return null;
            }

            var title = Read("title");
            if (string.IsNullOrWhiteSpace(title)) return null;

            DateTime? published = DateTime.TryParse(Read("publishedAt"), out var p) ? p : null;
            return new SourceArticleBrief(
                title, Read("summary"), Read("sourceName"), Read("sourceUrl"), published,
                Read("content"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Toàn văn cắt bớt trước khi nhét vào ExtraJson — không để phình cột.</summary>
    private const int MaxStoredContent = 6000;

    public static Dictionary<string, object?> ToJsonBlock(SourceArticleBrief brief) => new()
    {
        ["title"] = brief.Title,
        ["summary"] = brief.Summary,
        ["sourceName"] = brief.SourceName,
        ["sourceUrl"] = brief.SourceUrl,
        ["publishedAt"] = brief.PublishedAt?.ToString("O"),
        ["content"] = brief.Content is { Length: > MaxStoredContent }
            ? brief.Content[..MaxStoredContent]
            : brief.Content,
    };

    /// <summary>Toàn văn đưa vào prompt — đủ dày để viết đúng, không quá dài để tốn token.</summary>
    private const int MaxPromptContent = 4000;

    /// <summary>
    /// Luật viết BẢN TIN — thay hoàn toàn cho template bán hàng của Page.
    /// Template của Page viết cho bài quảng cáo khoá học nên luôn kéo theo CTA "Inbox ngay để
    /// được tư vấn" và hashtag thương hiệu; ghép vào bản tin thời sự thì thành quảng cáo trá
    /// hình, người đọc nhận ra ngay.
    /// </summary>
    private const string NewsRules =
        """
        ### ĐÂY LÀ BẢN TIN, KHÔNG PHẢI BÀI QUẢNG CÁO (ưu tiên cao nhất)
        - TUYỆT ĐỐI KHÔNG viết hashtag. Trường "hashtags" trong JSON phải trả về mảng RỖNG [].
        - TUYỆT ĐỐI KHÔNG viết lời mời chào bán hàng kiểu "Inbox ngay", "Đăng ký ngay",
          "Liên hệ để được tư vấn", không nhắc hotline, không nhắc khoá học của trung tâm.
          Trường "cta" phải trả về chuỗi RỖNG "".
        - Không tự chèn đường link nào — hệ thống sẽ tự gắn link bài gốc xuống cuối.
        - Giọng: đưa tin khách quan, bình tĩnh. Được dùng tối đa 2–3 emoji, đừng nhiều hơn.

        ### NỘI DUNG
        - Chỉ dùng dữ kiện CÓ trong tư liệu trên. Không thêm số liệu, tên người, tên trường,
          mốc thời gian hay phát ngôn nào không xuất hiện ở trên.
        - Không sao chép nguyên văn quá 1 câu liên tiếp — viết lại bằng lời của mình.
        - Trường "caption" viết 60–90 từ: một câu nêu sự việc, 2–3 gạch đầu dòng ý chính,
          một câu mời đọc bản đầy đủ.
        - Trường "bannerHeadline" là thứ QUAN TRỌNG NHẤT ở đây: viết MỘT CÂU nêu đúng cốt lõi
          bản tin, TỐI ĐA 80 KÝ TỰ (đếm cả dấu cách). Đây là dòng chữ sẽ hiện to trên nền màu
          Facebook nên phải tự đứng một mình mà người đọc vẫn hiểu chuyện gì.
          Viết như tít báo: cụ thể, không hô hào, không dấu chấm cuối câu.
        """;

    /// <summary>
    /// Khối tư liệu chèn lên ĐẦU prompt.
    ///
    /// Ràng buộc thay đổi theo LƯỢNG tư liệu thực có:
    /// - CÓ toàn văn (cào bằng trình duyệt): tư liệu thật vài nghìn ký tự → cho viết đủ dài
    ///   theo chuẩn Facebook, chỉ cấm thêm dữ kiện ngoài bài.
    /// - CHỈ có tóm tắt: system prompt mặc định đòi 80–160 từ mà tư liệu chỉ ~50 từ, đưa vào
    ///   thế nào cũng phải bịa cho đủ → buộc phải hạ trần độ dài xuống 60–110 từ.
    /// </summary>
    public static string BuildPromptBlock(SourceArticleBrief brief)
    {
        var hasFullText = !string.IsNullOrWhiteSpace(brief.Content) && brief.Content!.Trim().Length >= 400;

        var sb = new StringBuilder();
        sb.AppendLine("## TƯ LIỆU GỐC (nguồn báo — CHỈ được dùng dữ kiện trong đây)");
        sb.AppendLine($"Tiêu đề gốc: {brief.Title.Trim()}");
        if (!string.IsNullOrWhiteSpace(brief.SourceName))
            sb.AppendLine($"Nguồn: {brief.SourceName.Trim()}");
        if (brief.PublishedAt.HasValue)
            sb.AppendLine($"Ngày đăng gốc: {brief.PublishedAt.Value:dd/MM/yyyy}");

        if (hasFullText)
        {
            var content = brief.Content!.Trim();
            if (content.Length > MaxPromptContent) content = content[..MaxPromptContent] + "…";
            sb.AppendLine();
            sb.AppendLine("### NỘI DUNG BÀI BÁO");
            sb.AppendLine(content);
            sb.AppendLine();
            sb.AppendLine(NewsRules);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(brief.Summary))
                sb.AppendLine($"Tóm tắt gốc: {brief.Summary.Trim()}");
            sb.AppendLine();
            sb.AppendLine("### RÀNG BUỘC CHỐNG BỊA (ưu tiên cao nhất, ghi đè mọi hướng dẫn độ dài khác)");
            sb.AppendLine("- Tư liệu trên RẤT NGẮN. Chỉ được diễn giải lại những gì CÓ trong đó.");
            sb.AppendLine("- TUYỆT ĐỐI KHÔNG thêm: con số, tỉ lệ %, mốc thời gian, học phí, chỉ tiêu,");
            sb.AppendLine("  tên người, tên trường, tên cơ quan, phát ngôn, trích dẫn — nếu tư liệu không nêu.");
            sb.AppendLine("- Không viết như thể đã đọc toàn văn bài báo. Không dùng cụm \"theo bài viết\".");
            sb.AppendLine("- Thân bài 60–110 từ. Viết dài hơn sẽ buộc phải bịa.");
            sb.AppendLine("- Tư liệu quá mỏng thì viết khái quát rồi mời tương tác, KHÔNG lấp bằng chi tiết tự nghĩ.");
        }

        return sb.ToString().TrimEnd();
    }
}
