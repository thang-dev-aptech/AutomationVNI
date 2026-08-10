using Backend.Modules.ContentCrawl.Enums;
using Backend.Shared;

namespace Backend.Modules.ContentCrawl;

/// <summary>Một tin đã cào về, trước khi biến thành Post.</summary>
public class CrawledArticleModel : BaseEntity
{
    public Guid CrawlSourceId { get; set; }                       // No FK
    public Guid? CrawlRunId { get; set; }                         // No FK

    // ── Nội dung thô từ feed ────────────────────────────────────────────────
    public string Title { get; set; } = string.Empty;
    /// <summary>Tóm tắt ngắn (og:description) — dùng hiển thị và chấm trùng.</summary>
    public string? Summary { get; set; }
    /// <summary>
    /// TOÀN VĂN bài báo do trình duyệt bóc ra. Đây là tư liệu chính cho AI viết lại —
    /// khác hẳn thời dùng RSS khi chỉ có ~50 từ và AI buộc phải bịa cho đủ độ dài.
    /// </summary>
    public string? Content { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    /// <summary>URL đã bỏ query/utm/fragment — chống trùng rẻ nhất khi hai feed đăng lại cùng bài.</summary>
    public string? NormalizedUrl { get; set; }
    public string? SourceGuid { get; set; }                       // &lt;guid&gt; của feed
    public string? Author { get; set; }                           // dc:creator
    public string? SourceCategory { get; set; }
    /// <summary>CHỈ lưu URL, không tải ảnh về — ảnh thuộc bản quyền toà soạn.</summary>
    public string? ThumbnailUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime FetchedAt { get; set; }

    // ── Chống trùng ─────────────────────────────────────────────────────────
    public string ContentHash { get; set; } = string.Empty;       // SHA-256 hex của normalize(tiêu đề|tóm tắt)
    public long SimHash { get; set; }                             // 64-bit, lưu signed (SQLite INTEGER)
    public Guid? DuplicateOfId { get; set; }                      // No FK — CrawledArticles.Id HOẶC Posts.Id
    public DuplicateTarget DuplicateTarget { get; set; } = DuplicateTarget.None;
    public double? DuplicateScore { get; set; }                   // 0..1
    public DedupMethod DuplicateMethod { get; set; } = DedupMethod.None;
    /// <summary>Lý do của AI hoặc ghi chú kiểm toán. Giữ cả khi KHÔNG trùng để soi lại.</summary>
    public string? DuplicateReason { get; set; }

    // ── Trạng thái ──────────────────────────────────────────────────────────
    public CrawledArticleStatus Status { get; set; } = CrawledArticleStatus.New;
    public int DedupAttemptCount { get; set; }                    // chốt chặn lặp vô hạn
    public string? ErrorMessage { get; set; }
    public string? RejectReason { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? ResultBatchId { get; set; }                      // No FK — batch post sinh ra khi duyệt
    public int ResultPostCount { get; set; }

    // ── Bản nháp AI (để người duyệt đọc, KHÔNG phải bài đăng cuối) ──────────

    /// <summary>
    /// Số thứ tự nhỏ để gõ trên Telegram: 1, 2, 3… thay cho 6 ký tự hex.
    ///
    /// Cấp khi tin vào hàng CHỜ DUYỆT và giữ nguyên suốt thời gian nằm đó. Tin rời hàng
    /// (duyệt/bỏ) thì số được trả lại cho tin mới — nhờ vậy danh sách luôn là 1..N chứ không
    /// trôi dần lên hàng trăm. Đổi lại, một số CÓ THỂ trỏ sang tin khác sau khi tin cũ rời đi;
    /// chốt chặn là /dang luôn mở ô chọn page có hiện TIÊU ĐỀ trước khi đăng.
    /// </summary>
    public int? ShortCode { get; set; }

    // ── Chuyên mục ──────────────────────────────────────────────────────────
    /// <summary>
    /// Slug chuyên mục AI chọn từ danh sách ĐÓNG (xem NewsTaxonomy). Lưu cả slug lẫn CategoryId:
    /// slug là thứ dựng đường dẫn trang tĩnh, CategoryId là khoá để join. Chỉ lưu Id thì đổi
    /// slug làm chết URL bài cũ; chỉ lưu slug thì không join được với bảng Categories.
    /// </summary>
    public string? CategorySlug { get; set; }
    public Guid? CategoryId { get; set; }

    // ── Chấm điểm bằng AI ───────────────────────────────────────────────────
    /// <summary>0–100. null = chưa chấm được (AI hỏng) — KHÁC với chấm ra điểm thấp.</summary>
    public int? QualityScore { get; set; }
    public string? ScreenTopic { get; set; }
    public string? ScreenReason { get; set; }
    /// <summary>Tóm tắt 2-3 câu do AI viết, để người duyệt liếc trên Telegram là quyết được.</summary>
    public string? ScreenSummary { get; set; }

    // ── Telegram (Phase 2) ──────────────────────────────────────────────────
    /// <summary>
    /// Định danh SỰ VIỆC cụ thể, do AI sinh ngay trong lượt chấm điểm — không tốn lượt gọi
    /// riêng. Ví dụ <c>to-chuc-thi-lai-thpt-chuyen-tuyen-quang</c>.
    ///
    /// Cùng dạng chuỗi với <c>NewsArticles.EventKey</c> để đối chiếu được giữa tin mới cào và
    /// bài đã lên web.
    /// </summary>
    public string? EventKey { get; set; }

    public long? TelegramChatId { get; set; }
    public int? TelegramMessageId { get; set; }

    /// <summary>
    /// Duyệt từ Telegram thì đăng luôn, không dừng ở Approved chờ người vào web bấm lịch.
    /// Duyệt trên web KHÔNG bật cờ này — ở đó người dùng còn muốn xem lại bài trước khi đăng.
    /// </summary>
    public bool AutoPublishRequested { get; set; }

    /// <summary>
    /// Đã báo kết quả đăng về Telegram lúc nào. Đây là toàn bộ cơ chế chống báo trùng —
    /// worker quét lại mỗi vòng, không có cột này thì cứ mỗi 30 giây lại nhắn một lần.
    /// </summary>
    public DateTime? TelegramResultAt { get; set; }
}

/// <summary>
/// Chỉ mục vân tay dùng CHUNG cho tin đã cào và bài ĐÃ ĐĂNG — một đường truy vấn duy nhất
/// cho cả hai. Band là 4 lát 16-bit của SimHash, mỗi lát một index: hai vân tay cách nhau
/// ≤ 3 bit chắc chắn trùng ít nhất một lát (chuồng bồ câu), nên chặn được ứng viên mà
/// không phải quét cả bảng.
///
/// Bảng riêng thay vì thêm cột vào Posts: Posts là bảng nóng nhất hệ thống, và nhét vào
/// Post.ExtraJson thì không index được.
/// </summary>
public class ContentFingerprintModel : BaseEntity
{
    public FingerprintOwner OwnerType { get; set; }
    public Guid OwnerId { get; set; }                             // No FK
    public string ContentHash { get; set; } = string.Empty;
    public long SimHash { get; set; }
    public int Band0 { get; set; }
    public int Band1 { get; set; }
    public int Band2 { get; set; }
    public int Band3 { get; set; }
    /// <summary>Tiêu đề rút gọn để tính Jaccard và cho AI đọc, khỏi join ngược về bảng gốc.</summary>
    public string? TitleSnippet { get; set; }
    /// <summary>PublishedAt / FetchedAt — dùng để cắt cửa sổ thời gian khi tìm ứng viên.</summary>
    public DateTime ContentAt { get; set; }
    public Guid? SocialChannelId { get; set; }                    // No FK — Post mới có, article để null
}
