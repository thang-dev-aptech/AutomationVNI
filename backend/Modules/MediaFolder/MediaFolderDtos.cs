using Backend.Shared;

namespace Backend.Modules.MediaFolder;

public class CreateMediaFolderRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentFolderId { get; set; }
    public Guid? SocialChannelId { get; set; }
    public int SortOrder { get; set; }
}

public class UpdateMediaFolderRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Guid? ParentFolderId { get; set; }
    public Guid? SocialChannelId { get; set; }
    public int? SortOrder { get; set; }
}

public class MediaFolderFilterRequest : PagedFilterRequest
{
    public Guid? ParentFolderId { get; set; }
    public Guid? SocialChannelId { get; set; }
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
}

/// <summary>
/// MEDIA-06 (đã sửa lại): tạo cùng một folder gốc, cùng tên, ở nhiều Page cùng lúc
/// (vd: 100 Page → mỗi Page có 1 folder "Campaign X" ở gốc). Best-effort: Page này lỗi
/// không chặn Page khác — khác với BulkCreateMediaFolderRequest (MEDIA-03, hierarchy
/// trong MỘT Page, nguyên tử cả batch).
/// </summary>
public class CreateMediaFolderAcrossPagesRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<Guid> SocialChannelIds { get; set; } = [];
}

public class CreateMediaFolderAcrossPagesResultItem
{
    public Guid SocialChannelId { get; set; }
    public bool Success { get; set; }
    public Guid? FolderId { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CreateMediaFolderAcrossPagesResponse
{
    public int TotalRequested { get; set; }
    public int TotalSucceeded { get; set; }
    public int TotalFailed { get; set; }
    public List<CreateMediaFolderAcrossPagesResultItem> Results { get; set; } = [];
}

public class GetMediaFolderChildrenRequest
{
    public Guid SocialChannelId { get; set; }
    public Guid? ParentFolderId { get; set; }
    public int Index { get; set; } = 1;
    public int Size { get; set; } = 20;
    public string? SortBy { get; set; } = "sortOrder";
    public string? SortDirection { get; set; } = "asc";
}

public class GetMediaFolderBreadcrumbRequest
{
    public Guid SocialChannelId { get; set; }
    public Guid FolderId { get; set; }
}

public class MediaFolderBreadcrumbItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class MediaFolderBreadcrumbResponse
{
    /// <summary>Thứ tự từ gốc Page đến folder đích (gồm folder đích).</summary>
    public List<MediaFolderBreadcrumbItem> Ancestors { get; set; } = [];
}

public class SearchMediaFoldersRequest
{
    public Guid SocialChannelId { get; set; }
    public string? Keyword { get; set; }
    public int Index { get; set; } = 1;
    public int Size { get; set; } = 20;
    public string? SortBy { get; set; } = "name";
    public string? SortDirection { get; set; } = "asc";
}

public class MediaFolderSearchResultItem
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentFolderId { get; set; }
    public Guid? SocialChannelId { get; set; }
    public int SortOrder { get; set; }
    public int ChildFolderCount { get; set; }
    public int DirectAssetCount { get; set; }
    public int AssetCount { get; set; }
    public bool HasChildren { get; set; }

    /// <summary>Đường dẫn đầy đủ trong Page, phân biệt folder trùng tên.</summary>
    public string FullPath { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class MediaFolderResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentFolderId { get; set; }
    public Guid? SocialChannelId { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Số thư mục con trực tiếp (một cấp, không gồm descendants).</summary>
    public int ChildFolderCount { get; set; }

    /// <summary>Số media/ảnh trực tiếp trong folder (không tính thư mục con).</summary>
    public int DirectAssetCount { get; set; }

    /// <summary>Số ảnh trực tiếp trong folder — tương thích ngược với DirectAssetCount.</summary>
    public int AssetCount { get; set; }

    /// <summary>Có thư mục con hay không — tương thích UI hiện tại (ChildFolderCount > 0).</summary>
    public bool HasChildren { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public enum BulkDuplicatePolicy
{
    Error = 0,
    Skip = 1
}

public class BulkCreateMediaFolderItem
{
    /// <summary>Mã định danh tạm thời của client cho node này trong batch (ví dụ: "node-1", "0").</summary>
    public string ClientRef { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Tham chiếu tới ClientRef của node cha trong cùng batch.
    /// Nếu null, node này sẽ được gắn vào ParentFolderId của request (hoặc root nếu null).
    /// </summary>
    public string? ParentRef { get; set; }

    public int SortOrder { get; set; }
}

public class BulkCreateMediaFolderRequest
{
    /// <summary>Tất cả folder trong batch đều kế thừa SocialChannelId này (không nhận Page riêng từng node).</summary>
    public Guid SocialChannelId { get; set; }

    /// <summary>Thư mục cha gốc trong DB cho các node cấp cao nhất của batch (null = root của Page).</summary>
    public Guid? ParentFolderId { get; set; }

    /// <summary>Nếu true: chỉ kiểm tra hợp lệ và trả về preview/lỗi, không ghi vào database.</summary>
    public bool ValidateOnly { get; set; }

    /// <summary>Chính sách xử lý trùng tên trong cùng thư mục cha (Error hoặc Skip).</summary>
    public BulkDuplicatePolicy DuplicatePolicy { get; set; } = BulkDuplicatePolicy.Error;

    /// <summary>Danh sách các thư mục cần tạo.</summary>
    public List<BulkCreateMediaFolderItem> Folders { get; set; } = [];
}

public class BulkCreatedMediaFolderItem
{
    public string ClientRef { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentFolderId { get; set; }
    public Guid? SocialChannelId { get; set; }
    public int Depth { get; set; }
    public bool IsSkipped { get; set; }
}

public class BulkMediaFolderError
{
    public string? ClientRef { get; set; }
    public string? Name { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class BulkCreateMediaFolderResponse
{
    public bool Success { get; set; }
    public bool ValidateOnly { get; set; }
    public int TotalRequested { get; set; }
    public int TotalCreated { get; set; }
    public int TotalSkipped { get; set; }
    public List<BulkCreatedMediaFolderItem> Folders { get; set; } = [];
    public List<BulkMediaFolderError> Errors { get; set; } = [];
}
