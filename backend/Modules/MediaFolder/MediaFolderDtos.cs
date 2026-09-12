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

public class GetMediaFolderChildrenRequest
{
    public Guid SocialChannelId { get; set; }
    public Guid? ParentFolderId { get; set; }
    public int Index { get; set; } = 1;
    public int Size { get; set; } = 20;
    public string? SortBy { get; set; } = "sortOrder";
    public string? SortDirection { get; set; } = "asc";
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
