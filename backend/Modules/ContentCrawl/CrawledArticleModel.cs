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
    /// <summary>Tóm tắt ~50 từ đã gỡ HTML. RSS KHÔNG trả toàn văn — đây là tất cả tư liệu AI có.</summary>
    public string? Summary { get; set; }
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
    public int RewriteAttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RejectReason { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? ResultBatchId { get; set; }                      // No FK — batch post sinh ra khi duyệt
    public int ResultPostCount { get; set; }

    // ── Bản nháp AI (để người duyệt đọc, KHÔNG phải bài đăng cuối) ──────────
    public string? DraftContent { get; set; }
    public string? DraftExtraJson { get; set; }                   // hashtags / cta / imagePrompt

    // ── Telegram (Phase 2) ──────────────────────────────────────────────────
    public long? TelegramChatId { get; set; }
    public int? TelegramMessageId { get; set; }
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
