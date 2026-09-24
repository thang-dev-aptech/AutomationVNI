using Backend.Shared;

namespace Backend.Modules.MusicTrack;

/// <summary>
/// Thư viện nhạc dùng lại nhiều lần qua nhiều lượt render Reels — KHÔNG dùng chung
/// MediaAssetModel vì các trường của nó (MediaRole/CategoryIds/Width/Height...) đều là
/// khái niệm "ảnh/video gắn vào 1 post cụ thể", còn nhạc là tài nguyên thư viện độc lập.
/// </summary>
public class MusicTrackModel : BaseEntity
{
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
}
