using Backend.Shared;

namespace Backend.Modules.GoogleDrive;

/// <summary>
/// Hàng đợi retry cho file Google Drive tải/nhập lỗi. Mỗi dòng ứng với đúng một
/// <see cref="GoogleDriveFileId"/>; đạt GoogleDriveOptions.MaxRetryAttempts thì ngừng thử lại
/// vĩnh viễn cho file đó, nhưng dòng vẫn giữ lại để tra cứu thủ công qua LastError.
/// </summary>
public class GoogleDriveImportFailureModel : BaseEntity
{
    public string GoogleDriveFileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int AttemptCount { get; set; }
    public DateTime LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}
