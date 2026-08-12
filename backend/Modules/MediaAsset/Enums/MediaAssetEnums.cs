namespace Backend.Modules.MediaAsset.Enums;

public enum MediaSource
{
    Upload      = 1,
    AIGenerated = 2,
    Overlay     = 3
}

public enum MediaRole
{
    Primary    = 1,
    Thumbnail  = 2,
    Attachment = 3,
    Cover      = 4,

    /// <summary>
    /// Ảnh NGUỒN (từ MediaFolder) đang được chọn để render khung hình Reels — khác Attachment/Cover
    /// vốn là ảnh ĐÃ RENDER xong chữ. SortOrder = thứ tự khung hình trong video.
    /// </summary>
    TemplateSource = 5
}
