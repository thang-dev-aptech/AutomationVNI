using System.Text;
using System.Text.Json;

namespace Backend.Modules.Post;

/// <summary>Tư liệu gốc của một tin đã cào, đi kèm Post để AI viết đúng dữ kiện.</summary>
public sealed record SourceArticleBrief(
    string Title, string? Summary, string? SourceName, string? SourceUrl, DateTime? PublishedAt);

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
                title, Read("summary"), Read("sourceName"), Read("sourceUrl"), published);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Dictionary<string, object?> ToJsonBlock(SourceArticleBrief brief) => new()
    {
        ["title"] = brief.Title,
        ["summary"] = brief.Summary,
        ["sourceName"] = brief.SourceName,
        ["sourceUrl"] = brief.SourceUrl,
        ["publishedAt"] = brief.PublishedAt?.ToString("O"),
    };

    /// <summary>
    /// Khối tư liệu chèn lên ĐẦU prompt.
    ///
    /// Phần ràng buộc chống bịa là thứ quan trọng nhất ở đây: system prompt mặc định yêu cầu
    /// thân bài 80–160 từ, mà tư liệu RSS chỉ ~50 từ. Đưa 50 từ vào rồi đòi 160 từ ra là
    /// BẢO ĐẢM model phải bịa cho đủ. Nên phải hạ giới hạn độ dài xuống và liệt kê rõ những
    /// thứ tuyệt đối không được tự nghĩ ra.
    /// </summary>
    public static string BuildPromptBlock(SourceArticleBrief brief)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## TƯ LIỆU GỐC (nguồn báo — CHỈ được dùng dữ kiện trong đây)");
        sb.AppendLine($"Tiêu đề gốc: {brief.Title.Trim()}");
        if (!string.IsNullOrWhiteSpace(brief.Summary))
            sb.AppendLine($"Tóm tắt gốc: {brief.Summary.Trim()}");
        if (!string.IsNullOrWhiteSpace(brief.SourceName))
            sb.AppendLine($"Nguồn: {brief.SourceName.Trim()}");
        if (brief.PublishedAt.HasValue)
            sb.AppendLine($"Ngày đăng gốc: {brief.PublishedAt.Value:dd/MM/yyyy}");
        sb.AppendLine();
        sb.AppendLine("### RÀNG BUỘC CHỐNG BỊA (ưu tiên cao nhất, ghi đè mọi hướng dẫn độ dài khác)");
        sb.AppendLine("- Tư liệu trên RẤT NGẮN (~50 từ). Chỉ được diễn giải lại những gì CÓ trong đó.");
        sb.AppendLine("- TUYỆT ĐỐI KHÔNG thêm: con số, tỉ lệ %, mốc thời gian, học phí, chỉ tiêu,");
        sb.AppendLine("  tên người, tên trường, tên cơ quan, phát ngôn, trích dẫn — nếu tư liệu không nêu.");
        sb.AppendLine("- Không viết như thể đã đọc toàn văn bài báo. Không dùng cụm \"theo bài viết\".");
        sb.AppendLine("- Thân bài 60–110 từ. Viết dài hơn sẽ buộc phải bịa.");
        sb.AppendLine("- Bố cục: nêu sự việc → 1 câu vì sao đáng quan tâm với phụ huynh/học sinh → CTA của Page.");
        sb.AppendLine("- Tư liệu quá mỏng thì viết khái quát rồi mời tương tác, KHÔNG lấp bằng chi tiết tự nghĩ.");
        return sb.ToString().TrimEnd();
    }
}
