namespace Backend.Shared.Backup;

/// <summary>
/// Cấu hình sao lưu CSDL. Cấu hình dưới khoá <c>DatabaseBackup</c>.
/// </summary>
public class DatabaseBackupOptions
{
    /// <summary>
    /// Tắt thì KHÔNG có bản sao nào. Chỉ tắt khi đã có cơ chế sao lưu khác ở tầng hạ tầng
    /// (snapshot ổ đĩa, sao lưu của nhà cung cấp VPS…).
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Thư mục chứa bản sao. Tương đối thì tính từ thư mục chạy ứng dụng.</summary>
    public string OutputPath { get; set; } = "Data/backups";

    /// <summary>Cách nhau bao nhiêu giờ giữa hai lần sao lưu.</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>
    /// Giữ bao nhiêu bản gần nhất. 14 bản × 30MB ≈ 420MB — chấp nhận được, và đủ để lùi lại
    /// hai tuần nếu phát hiện dữ liệu hỏng muộn.
    /// </summary>
    public int KeepCount { get; set; } = 14;

    /// <summary>
    /// Dừng sao lưu khi ổ còn ít hơn ngần này MB.
    ///
    /// Sao lưu làm đầy ổ rồi khiến CSDL không ghi được nữa là đổi một rủi ro lấy một tai nạn
    /// tệ hơn: mất bản sao thì còn CSDL, ổ đầy thì mất cả hai.
    /// </summary>
    public int MinFreeDiskMb { get; set; } = 500;
}
