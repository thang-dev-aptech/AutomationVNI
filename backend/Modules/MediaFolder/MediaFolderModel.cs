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

    public Guid? ParentFolderId { get; set; }

    /// <summary>Gắn trực tiếp với page (SocialChannel) — không qua PageContext, vì không phải
    /// page nào cũng đã setup PageContext nhưng page nào cũng có SocialChannelId.</summary>
    public Guid? SocialChannelId { get; set; }

    public int SortOrder { get; set; }
}
