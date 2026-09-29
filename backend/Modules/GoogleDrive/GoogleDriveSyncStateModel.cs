using Backend.Shared;

namespace Backend.Modules.GoogleDrive;

/// <summary>
/// Trạng thái đồng bộ dùng chung cho pipeline nhập file Google Drive.
/// Bảng này luôn chỉ có một dòng với <see cref="SingletonId"/>.
/// </summary>
public class GoogleDriveSyncStateModel : BaseEntity
{
    public static readonly Guid SingletonId = Guid.Parse("b3e6c8a1-6d1e-4f5c-9a34-6d2f1e8b9c02");

    public bool IsEnabled { get; set; } = true;
    public string? UpdatedByUserName { get; set; }

    /// <summary>Con trỏ bền vững của Drive changes.list — advance mỗi tick kể cả khi rỗng.</summary>
    public string? PageToken { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public int LastImportedCount { get; set; }

    /// <summary>
    /// GDRIVE-04: MediaFolder chuyên dụng (Name="Google Drive", SocialChannelId=null) chứa mọi
    /// MediaAsset Source=GoogleDrive. Null cho đến lần get-or-create đầu tiên.
    /// </summary>
    public Guid? DedicatedFolderId { get; set; }
}
