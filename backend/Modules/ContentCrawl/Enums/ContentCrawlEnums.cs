namespace Backend.Modules.ContentCrawl.Enums;

public enum CrawlSourceType
{
    /// <summary>Trang chuyên mục của báo — mở bằng trình duyệt OpenClaw, bóc TOÀN VĂN từng bài.</summary>
    WebPage = 1,
    /// <summary>Phase 3 — fanpage Facebook, dùng profile trình duyệt giữ phiên đăng nhập.</summary>
    FacebookPage = 2
}

public enum CrawlRunStatus
{
    Running = 1,
    Completed = 2,
    Failed = 3
}

/// <summary>
/// Vòng đời một tin đã cào. Deduping là trạng thái đang xử lý — worker phải có chốt chặn
/// đếm số lần thử, kẻo một bài lỗi lặp vô hạn (xem PostGenerationWorker).
/// </summary>
public enum CrawledArticleStatus
{
    New = 1,          // vừa cào về, chưa chấm trùng
    Deduping = 2,     // worker đang chấm trùng
    Rewriting = 3,    // KHÔNG dùng nữa (đã bỏ xào nháp). Giữ giá trị để dòng cũ trong DB
                      // không thành số lạ; worker quét nốt chúng sang Pending.
    Pending = 4,      // sạch, chờ người duyệt
    Duplicate = 5,    // bị loại vì trùng
    Filtered = 6,     // bị loại bởi từ khoá chặn hoặc quá cũ
    Approved = 7,     // đã duyệt, đã fan-out ra post
    Rejected = 8,     // người duyệt loại
    Failed = 9        // lỗi khi fan-out
}

public enum DedupMethod
{
    None = 0,
    SourceGuid = 1,   // trùng guid feed hoặc trùng URL đã chuẩn hoá
    ExactHash = 2,    // SHA-256 của (tiêu đề + tóm tắt) đã chuẩn hoá
    SimHash = 3,      // gần giống: khoảng cách Hamming hoặc Jaccard tiêu đề
    AiJudge = 4,      // AI phán khi điểm nằm vùng nghi ngờ
    Manual = 5        // người duyệt tự đánh dấu / gỡ đánh dấu
}

public enum DuplicateTarget
{
    None = 0,
    CrawledArticle = 1,
    PublishedPost = 2
}

public enum FingerprintOwner
{
    CrawledArticle = 1,
    Post = 2
}
