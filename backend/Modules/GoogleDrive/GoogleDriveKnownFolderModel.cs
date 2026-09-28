using Backend.Shared;

namespace Backend.Modules.GoogleDrive;

/// <summary>
/// GDRIVE-02: tập thư mục Google Drive đã biết (lớn dần qua changes.list). Root FolderId luôn
/// được EnsureRootFolderKnownAsync ghi vào từ tick đầu; thư mục con chỉ được thêm khi có parent
/// nằm trong tập này. Không backfill lịch sử — chỉ thư mục tạo SAU khi bật tính năng.
/// </summary>
public class GoogleDriveKnownFolderModel : BaseEntity
{
    /// <summary>Id thư mục trên Google Drive (unique).</summary>
    public string FolderId { get; set; } = string.Empty;

    /// <summary>Lúc hệ thống lần đầu nhận diện thư mục này.</summary>
    public DateTime DiscoveredAt { get; set; }
}
