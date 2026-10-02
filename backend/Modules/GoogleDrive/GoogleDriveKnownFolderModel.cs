using Backend.Shared;

namespace Backend.Modules.GoogleDrive;

/// <summary>
/// GDRIVE-02/05: bản đồ ánh xạ Drive FolderId → MediaFolder. Root dedicated (GDRIVE-04) cũng
/// có 1 dòng ở đây. Thư mục con chỉ được thêm khi cha nằm trong bản đồ (không backfill lịch sử
/// trừ reconcile full-tree ở GDRIVE-05 Task B).
/// </summary>
public class GoogleDriveKnownFolderModel : BaseEntity
{
    /// <summary>Id thư mục trên Google Drive (unique).</summary>
    public string FolderId { get; set; } = string.Empty;

    /// <summary>MediaFolder tương ứng trong app.</summary>
    public Guid MediaFolderId { get; set; }

    /// <summary>Parent trực tiếp trên Drive (null với root dedicated).</summary>
    public string? DriveParentId { get; set; }

    /// <summary>Tên thư mục lần cuối đồng bộ từ Drive.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Lúc hệ thống lần đầu nhận diện thư mục này.</summary>
    public DateTime DiscoveredAt { get; set; }
}
