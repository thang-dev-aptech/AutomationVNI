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
    public Guid? FolderId { get; set; }
    /// <summary>Loại bài (Categories) ảnh áp dụng — đa trị. Rỗng = dùng chung.</summary>
    public List<Guid>? CategoryIds { get; set; }
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
    /// <summary>Cập nhật loại bài áp dụng (đa trị). Gửi mảng rỗng để xoá hết.</summary>
    public List<Guid>? CategoryIds { get; set; }
}

public class MediaAssetFilterRequest : PagedFilterRequest
{
    public MediaSource? Source { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? FolderId { get; set; }

    /// <summary>Lọc ảnh áp dụng cho 1 loại bài (Categories) — khớp phần tử trong CategoryIds.</summary>
    public Guid? AppliesToCategoryId { get; set; }

    /// <summary>Chỉ lấy ảnh chưa thuộc thư mục nào (FolderId = null) — cho mục "Chưa phân loại".</summary>
    public bool? Unassigned { get; set; }
    public string? MimeType { get; set; }
}

/// <summary>Kéo-thả ảnh vào thư mục: chuyển nhiều ảnh vào 1 folder (null = đưa về "Chưa phân loại").</summary>
public class MoveMediaAssetsRequest
{
    public List<Guid> Ids { get; set; } = [];
    public Guid? FolderId { get; set; }
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
    public Guid? FolderId { get; set; }
    public List<Guid> CategoryIds { get; set; } = [];
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
