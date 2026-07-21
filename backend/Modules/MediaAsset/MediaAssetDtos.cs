using Backend.Modules.MediaAsset.Enums;
using Backend.Shared;

namespace Backend.Modules.MediaAsset;

public class CreateMediaAssetRequest
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

public class UpdateMediaAssetRequest
{
    public string? AltText { get; set; }
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public string? PublicUrl { get; set; }
    public Guid? CategoryId { get; set; }
}

public class MediaAssetFilterRequest : PagedFilterRequest
{
    public MediaSource? Source { get; set; }
    public Guid? CategoryId { get; set; }
    public string? MimeType { get; set; }
}

public class MediaAssetResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? OriginalFileName { get; set; }
    public string? PublicUrl { get; set; }
    public string PreviewUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public MediaSource Source { get; set; }
    public Guid? CategoryId { get; set; }
    public string? AltText { get; set; }
    public string? Description { get; set; }
    public string? Tags { get; set; }
    public List<string> Keywords { get; set; } = [];
    public int? Width { get; set; }
    public int? Height { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreatePostMediaRequest
{
    public Guid PostId { get; set; }
    public Guid MediaId { get; set; }
    public MediaRole MediaRole { get; set; } = MediaRole.Primary;
    public int SortOrder { get; set; }
}

public class UpdatePostMediaRequest
{
    public MediaRole? MediaRole { get; set; }
    public int? SortOrder { get; set; }
}

public class PostMediaFilterRequest : PagedFilterRequest
{
    public Guid? PostId { get; set; }
    public Guid? MediaId { get; set; }
    public MediaRole? MediaRole { get; set; }
}

public class PostMediaResponse
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid MediaId { get; set; }
    public MediaRole MediaRole { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}
