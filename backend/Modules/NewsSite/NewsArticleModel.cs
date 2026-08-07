using Backend.Shared;

namespace Backend.Modules.NewsSite;

public enum NewsArticleStatus
{
    /// <summary>AI đang viết. Có chốt đếm số lần thử, không để kẹt vĩnh viễn.</summary>
    Composing = 1,
    /// <summary>Viết xong, chờ dựng trang tĩnh.</summary>
    Ready = 2,
    /// <summary>Đã có file HTML trên đĩa — chỉ trạng thái này mới được dán link lên Facebook.</summary>
    Published = 3,
    /// <summary>Ẩn khỏi trang chủ nhưng giữ file để URL cũ không chết.</summary>
    Hidden = 4,
    Failed = 5,
}

/// <summary>
/// Một bài trên trang tin của mình (tintuc.vni.edu.vn).
///
/// Vì sao là entity riêng, không dùng PostModel: PostModel gắn cứng với MỘT SocialChannelId và
/// fan-out tạo N dòng cho N page. Bài website chỉ có ĐÚNG MỘT bản, một đường dẫn, và nhiều post
/// Facebook cùng trỏ về nó. Nhét vào Posts thì phải bịa một kênh giả, và Posts là bảng nóng
/// nhất hệ thống.
/// </summary>
public class NewsArticleModel : BaseEntity
{
    /// <summary>
    /// Đường dẫn thân thiện. SINH ĐÚNG MỘT LẦN rồi đóng băng — sửa tiêu đề mà slug đổi theo là
    /// URL cũ chết, trong khi Facebook vẫn giữ thẻ og trỏ vào URL đó nhiều ngày.
    /// </summary>
    public string Slug { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>Sapo 2–3 câu. Dùng cho card ngoài trang chủ và og:description.</summary>
    public string? Sapo { get; set; }

    /// <summary>Thân bài đã dựng HTML an toàn (chỉ p / h2 / ul / li / strong).</summary>
    public string BodyHtml { get; set; } = "";

    /// <summary>JSON array — "điều cần nhớ", cũng là chất liệu rút caption Facebook.</summary>
    public string? KeyPointsJson { get; set; }

    /// <summary>JSON array {time, what} — khối "Mốc cần nhớ". Null khi bài không có mốc nào.</summary>
    public string? TimelineJson { get; set; }

    public string? CategorySlug { get; set; }
    public Guid? CategoryId { get; set; }

    /// <summary>Tính từ số từ, KHÔNG hỏi AI — hỏi thì nó đoán và ra số lẻ vô lý.</summary>
    public int ReadMinutes { get; set; }

    // ── Dẫn nguồn: bắt buộc, không phải tuỳ chọn ───────────────────────────
    public string? SourceName { get; set; }
    public string? SourceUrl { get; set; }

    public NewsArticleStatus Status { get; set; } = NewsArticleStatus.Composing;
    public DateTime? PublishedAt { get; set; }
    public DateTime? LastBuiltAt { get; set; }
    public int ViewCount { get; set; }

    /// <summary>Tin đã cào sinh ra bài này. No FK — để xoá tin cũ không kéo bài web theo.</summary>
    public Guid? CrawledArticleId { get; set; }

    /// <summary>Chốt chặn lặp vô hạn, cùng khuôn DedupAttemptCount của luồng cào.</summary>
    public int ComposeAttemptCount { get; set; }

    public string? ErrorMessage { get; set; }
}
