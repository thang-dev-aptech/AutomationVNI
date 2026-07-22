using Backend.Shared;

namespace Backend.Modules.MediaFolder;

/// <summary>
/// Thư mục phân loại kho Media, lồng nhiều cấp qua ParentFolderId (không FK constraint).
/// Ảnh trỏ về folder qua MediaAsset.FolderId; folder gốc có ParentFolderId = null.
/// </summary>
public class MediaFolderModel : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Thư mục cha — null = thư mục gốc. Quan hệ logic, không FK.</summary>
    public Guid? ParentFolderId { get; set; }

    public int SortOrder { get; set; }
}
