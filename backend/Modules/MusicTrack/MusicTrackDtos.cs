using Backend.Shared;

namespace Backend.Modules.MusicTrack;

public class CreateMusicTrackRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

public class UpdateMusicTrackRequest
{
    public string? DisplayName { get; set; }
}

public class MusicTrackFilterRequest : PagedFilterRequest
{
}

public class MusicTrackResponse
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
}
