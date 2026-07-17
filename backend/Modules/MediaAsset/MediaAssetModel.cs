using Backend.Modules.MediaAsset.Enums;
using Backend.Shared;

namespace Backend.Modules.MediaAsset;

public class MediaAssetModel : BaseEntity
{
    public string FileName { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string? PublicUrl { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public MediaSource Source { get; set; } = MediaSource.Upload;
    public Guid? CategoryId { get; set; }
    public string? AltText { get; set; }
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
}

public class PostMediaModel : BaseEntity
{
    public Guid PostId { get; set; }
    public Guid MediaId { get; set; }
    public MediaRole MediaRole { get; set; } = MediaRole.Primary;
    public int SortOrder { get; set; }
}
