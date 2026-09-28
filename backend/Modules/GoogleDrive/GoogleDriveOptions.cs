namespace Backend.Modules.GoogleDrive;

/// <summary>
/// Cấu hình worker nhập file từ Google Drive. Mặc định Enabled=false giống
/// ContentCrawl/Scheduler/GenerationWorker — bật riêng ở appsettings.Development.json và biến
/// môi trường production.
/// </summary>
public class GoogleDriveOptions
{
    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; } = 180;

    /// <summary>Id của MỘT folder Google Drive dùng chung — nơi người dùng thả file vào.</summary>
    public string FolderId { get; set; } = string.Empty;

    /// <summary>
    /// Đường dẫn tới file khoá Google Service Account (JSON). KHÔNG BAO GIỜ đặt giá trị thật ở
    /// đây hay trong appsettings.json — chỉ qua appsettings.Production.json (đã gitignore) hoặc
    /// biến môi trường / đường dẫn mount ngoài trên server.
    /// </summary>
    public string CredentialsPath { get; set; } = string.Empty;

    /// <summary>Số file tải/nhập tối đa mỗi tick — chặn một lượt đổ hàng trăm file cùng lúc.</summary>
    public int MaxFilesPerTick { get; set; } = 20;

    /// <summary>Quá số lần này mà một file vẫn lỗi tải thì ngừng thử lại vĩnh viễn cho file đó.</summary>
    public int MaxRetryAttempts { get; set; } = 5;
}
