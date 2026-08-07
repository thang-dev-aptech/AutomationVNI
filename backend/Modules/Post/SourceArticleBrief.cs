using System.Text;
using System.Text.Json;

namespace Backend.Modules.Post;

/// <summary>
/// Tư liệu gốc của một tin đã cào, đi kèm Post để AI viết đúng dữ kiện.
/// <paramref name="Content"/> là TOÀN VĂN bài báo (có thể null nếu bóc hỏng).
/// </summary>
public sealed record SourceArticleBrief(
    string Title, string? Summary, string? SourceName, string? SourceUrl, DateTime? PublishedAt,
    string? Content = null,
    /// <summary>
    /// Góc nhìn được giao cho page này. Mỗi page một góc khác nhau để tiêu đề không na ná.
    /// Đo thực tế: để AI tự do với tiêu đề 60 ký tự thì hai page ra "…được cập nhật sát giờ
    /// lọc ảo" và "…biến động sát giờ lọc ảo" — khác đúng một cụm từ.
    /// </summary>
    string? Angle = null,

    /// <summary>
    /// Các ý chính của BÀI TRÊN WEB của mình. Có giá trị nghĩa là caption Facebook được rút
    /// từ bài đã viết, KHÔNG phải viết lại từ toàn văn báo gốc.
    ///
    /// Đây là chỗ tiết kiệm lớn nhất: trước đây mỗi page sinh lại từ toàn văn 4.000 ký tự,
    /// prompt trung bình 12.726 ký tự ≈ 9.000 token, NHÂN cho mỗi page. Rút từ ý chính thì
    /// prompt còn ~800 token — 12 page giảm từ 108.000 xuống 18.600 token.
    ///
    /// Đặt CUỐI record là cố ý: chèn vào giữa sẽ làm lệch mọi lời gọi theo vị trí đang có.
    /// </summary>
    List<string>? KeyPoints = null);

/// <summary>Các góc nhìn chia luân phiên cho từng page trong cùng một lượt duyệt.</summary>
public static class HeadlineAngles
{
    /// <summary>
    /// Mỗi góc RÀNG BUỘC CÁCH MỞ ĐẦU CÂU, không chỉ gợi ý nhấn mạnh.
    /// Đo thực tế: bản đầu chỉ ghi "nhấn vào TÁC ĐỘNG" thì hai page vẫn ra
    /// "32/35 sinh viên bị phát hiện dùng AI nhờ câu lệnh ẩn" và "…làm bài giữa kỳ"
    /// — cùng mở bằng con số, khác đúng cụm cuối. Phải CẤM thẳng lối mở của góc khác
    /// thì model mới đổi cấu trúc câu.
    /// </summary>
    public static readonly string[] All =
    [
        "MỞ ĐẦU bằng CON SỐ hoặc tỉ lệ nổi bật nhất trong tin, rồi mới tới sự việc.",

        "MỞ ĐẦU bằng ĐỐI TƯỢNG chịu ảnh hưởng (phụ huynh / học sinh / thí sinh / giáo viên) "
        + "và nêu rõ họ được gì hoặc phải làm gì. CẤM mở đầu bằng con số.",

        "Viết thành MỘT CÂU HỎI, kết thúc bằng dấu hỏi. CẤM mở đầu bằng con số.",

        "MỞ ĐẦU bằng mốc thời gian hoặc tính cấp bách (Từ ngày…, Còn… , Sát giờ…). "
        + "CẤM mở đầu bằng con số không phải ngày tháng.",

        "MỞ ĐẦU bằng CƠ QUAN hoặc TRƯỜNG ra quyết định, rồi mới tới nội dung quyết định. "
        + "CẤM mở đầu bằng con số.",
    ];

    /// <summary>
    /// Góc cho kênh thứ <paramref name="index"/> của một tin.
    ///
    /// <paramref name="seedText"/> (tiêu đề tin) xoay điểm bắt đầu. Không có nó thì kênh đầu
    /// tiên LUÔN nhận góc "CON SỐ" ở mọi tin — page đó thành ra bài nào cũng mở bằng con số,
    /// đọc một tuần là nhận ra khuôn.
    ///
    /// Băm bằng FNV-1a chứ không phải string.GetHashCode(): .NET randomize GetHashCode mỗi
    /// tiến trình, góc sẽ nhảy sau mỗi lần restart và bài sinh lại khác bài gốc không lý do.
    /// </summary>
    public static string ForIndex(int index, string? seedText = null)
        => All[(int)(((ulong)Math.Abs(index) + StableSeed(seedText)) % (ulong)All.Length)];

    private static ulong StableSeed(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var hash = 14695981039346656037UL;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 1099511628211UL;
        }
        return hash;
    }
}

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

            List<string>? keyPoints = null;
            foreach (var prop in block.EnumerateObject())
            {
                if (!prop.Name.Equals("keyPoints", StringComparison.OrdinalIgnoreCase)) continue;
                if (prop.Value.ValueKind != JsonValueKind.Array) break;
                keyPoints = prop.Value.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .ToList();
                break;
            }

            DateTime? published = DateTime.TryParse(Read("publishedAt"), out var p) ? p : null;
            return new SourceArticleBrief(
                title, Read("summary"), Read("sourceName"), Read("sourceUrl"), published,
                Read("content"), Read("angle"), keyPoints);
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
        ["angle"] = brief.Angle,
        ["keyPoints"] = brief.KeyPoints,
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
        // ── Nhánh 1: rút caption TỪ BÀI ĐÃ VIẾT trên web ───────────────────────
        // Đặt trước hai nhánh kia vì nó rẻ nhất và chính xác nhất: bài web đã qua bộ soi dữ
        // kiện nên mọi con số trong đó đều truy được về nguồn. Không cần đưa lại toàn văn báo
        // gốc — đó là 4.000 ký tự × mỗi page bị đốt vô ích.
        if (brief.KeyPoints is { Count: > 0 })
            return BuildFromSiteArticle(brief);

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
            AppendAngle(sb, brief);
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
            AppendAngle(sb, brief);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Prompt rút caption từ bài đã đăng trên website của mình.
    ///
    /// Ngắn hơn hẳn nhánh toàn văn, và AN TOÀN HƠN: bài web đã qua bộ soi dữ kiện nên không
    /// còn số bịa; caption chỉ được dùng lại ý trong đó nên cũng sạch theo.
    /// </summary>
    private static string BuildFromSiteArticle(SourceArticleBrief brief)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## BÀI ĐÃ ĐĂNG TRÊN WEBSITE CỦA CHÚNG TÔI");
        sb.AppendLine($"Tiêu đề: {brief.Title.Trim()}");
        if (!string.IsNullOrWhiteSpace(brief.Summary))
            sb.AppendLine($"Sapo: {brief.Summary.Trim()}");
        sb.AppendLine();
        sb.AppendLine("Ý chính:");
        foreach (var k in brief.KeyPoints!) sb.AppendLine($"- {k}");
        sb.AppendLine();
        sb.AppendLine(NewsRules);
        sb.AppendLine();
        sb.AppendLine("### VIẾT GÌ");
        sb.AppendLine("- CHỈ dùng ý trong bài trên. Không thêm dữ kiện nào khác.");
        sb.AppendLine("- Không nhắc tên báo gốc — đây là bài của chúng tôi.");
        sb.AppendLine("- Không tự chèn link; hệ thống tự gắn link bài xuống bình luận.");
        AppendAngle(sb, brief);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Ép bannerHeadline đi theo góc được giao. Đặt CUỐI prompt để nó là thứ model đọc
    /// gần nhất trước khi viết, và nhắc lại tiêu đề gốc kèm lệnh cấm bám theo — nếu không
    /// model chỉ đổi vài chữ cuối của tít gốc rồi coi như xong.
    /// </summary>
    private static void AppendAngle(StringBuilder sb, SourceArticleBrief brief)
    {
        if (string.IsNullOrWhiteSpace(brief.Angle)) return;

        sb.AppendLine();
        sb.AppendLine("### GÓC VIẾT BẮT BUỘC CHO bannerHeadline (ưu tiên cao nhất, ghi đè mọi hướng dẫn trên)");
        sb.AppendLine($"- {brief.Angle.Trim()}");
        sb.AppendLine("- Cùng tin này đang được viết cho nhiều Page, mỗi Page một góc mở đầu khác nhau.");
        sb.AppendLine("  Viết sai góc thì hai Page ra tít gần giống nhau — đó là lỗi nặng nhất ở đây.");
        sb.AppendLine($"- CẤM sao chép cấu trúc câu của tiêu đề gốc (\"{brief.Title.Trim()}\").");
        sb.AppendLine("  Đổi vài chữ cuối là KHÔNG đạt — phải khác cả cách mở đầu.");
    }
}
