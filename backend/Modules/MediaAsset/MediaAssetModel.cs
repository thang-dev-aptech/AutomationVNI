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

    /// <summary>Thư mục chứa ảnh — null = "Chưa phân loại". Quan hệ logic, không FK.</summary>
    public Guid? FolderId { get; set; }

    /// <summary>
    /// "Áp dụng cho loại bài nào" — JSON array Guid trỏ Categories (đa trị). Null/rỗng = dùng chung mọi loại.
    /// Nhánh 2 (MediaMatch) lọc ảnh theo Post.CategoryId khớp phần tử trong mảng này. Không FK.
    /// </summary>
    public string? CategoryIds { get; set; }
    public string? AltText { get; set; }
    public string? Description { get; set; }
    public string? Tags { get; set; }

    /// <summary>Caption Facebook 5 dòng (nối "\n") do AI sinh khi người dùng bấm nút — cột riêng,
    /// không đụng AltText/Description/Tags (MEDIA-CAPTION-01).</summary>
    public string? Caption { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>Id file gốc trên Google Drive (GDRIVE-01) — null cho mọi nguồn khác. Dùng để chống nhập trùng.</summary>
    public string? GoogleDriveFileId { get; set; }
}

public class PostMediaModel : BaseEntity
{
    public Guid PostId { get; set; }
    public Guid MediaId { get; set; }
    public MediaRole MediaRole { get; set; } = MediaRole.Primary;
    public int SortOrder { get; set; }
}
