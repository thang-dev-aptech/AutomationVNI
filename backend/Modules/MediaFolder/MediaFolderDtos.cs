using Backend.Shared;

namespace Backend.Modules.MediaFolder;

public class CreateMediaFolderRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentFolderId { get; set; }
    public int SortOrder { get; set; }
}

public class UpdateMediaFolderRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Guid? ParentFolderId { get; set; }
    public int? SortOrder { get; set; }
}

public class MediaFolderFilterRequest : PagedFilterRequest
{
    public Guid? ParentFolderId { get; set; }
}

public class MediaFolderResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentFolderId { get; set; }
    public int SortOrder { get; set; }

    /// <summary>Số ảnh trực tiếp trong folder (không tính thư mục con).</summary>
    public int AssetCount { get; set; }

    /// <summary>Có thư mục con hay không — để UI hiện nút mở rộng.</summary>
    public bool HasChildren { get; set; }

    public DateTime CreatedAt { get; set; }
}
