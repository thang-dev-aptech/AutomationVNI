using Backend.Shared;

namespace Backend.Modules.Notification;

/// <summary>Loại việc đã xảy ra. Dùng để chọn icon và lọc, không chứa logic nghiệp vụ.</summary>
public enum NotificationKind
{
    CrawlStarted = 1,
    CrawlFinished = 2,
    ArticleApproved = 3,
    ArticleRejected = 4,
    PostPublished = 5,
    PostFailed = 6,
}

/// <summary>Ai gây ra việc đó — thứ quan trọng nhất để tránh làm trùng nhau.</summary>
public enum NotificationSource
{
    System = 1,     // worker tự chạy theo lịch
    Web = 2,        // người dùng bấm trên giao diện
    Telegram = 3,   // sếp thao tác qua bot
}

/// <summary>
/// Một dòng nhật ký hoạt động hiện ở chuông trên thanh trên cùng.
///
/// Vì sao ĐỌC/CHƯA ĐỌC để chung cho cả nhóm chứ không tách theo người: nhóm dùng hệ thống này
/// chỉ vài người và mục đích là "biết người kia vừa làm gì để mình khỏi làm lại". Tách theo
/// người thì phải thêm bảng nối và mỗi lần đọc là một dòng ghi mới — đổi lấy thứ không ai cần.
/// Đánh đổi phải biết: một người bấm "đọc hết" thì chuông của cả nhóm về 0.
/// </summary>
public class AppNotificationModel : BaseEntity
{
    public NotificationKind Kind { get; set; }
    public NotificationSource Source { get; set; }

    /// <summary>Tên người hoặc "Telegram"/"Hệ thống". Hiện thẳng trên dòng thông báo.</summary>
    public string Actor { get; set; } = "";

    public string Title { get; set; } = "";
    public string? Message { get; set; }

    /// <summary>Đường dẫn trong app để bấm vào xem, ví dụ /crawl hoặc /bulk/{batchId}.</summary>
    public string? LinkUrl { get; set; }

    /// <summary>Id của tin/bài liên quan — để sau này gộp hoặc lọc, không hiện ra ngoài.</summary>
    public Guid? RefId { get; set; }

    public DateTime? ReadAt { get; set; }
}
