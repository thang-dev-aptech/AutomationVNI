namespace Backend.Modules.MediaCaption;

public class CreateMediaCaptionJobRequest { public Guid FolderId { get; set; } }

public class MediaCaptionJobItemResponse
{
    public Guid Id { get; set; }
    public Guid MediaAssetId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string PreviewUrl { get; set; } = string.Empty;
}

public class MediaCaptionJobResponse
{
    public Guid Id { get; set; }
    public Guid FolderId { get; set; }
    public string FolderName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? FinishedAt { get; set; }
    public List<MediaCaptionJobItemResponse> Items { get; set; } = [];
}
