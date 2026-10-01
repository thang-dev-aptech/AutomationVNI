using Backend.Shared;

namespace Backend.Modules.MediaCaption;

public enum MediaCaptionJobStatus { Queued, Running, Completed }
public enum MediaCaptionJobItemStatus { Pending, Running, Succeeded, Failed, Skipped }

public class MediaCaptionJobModel : BaseEntity
{
    public Guid FolderId { get; set; }
    public string FolderName { get; set; } = string.Empty;
    public MediaCaptionJobStatus Status { get; set; } = MediaCaptionJobStatus.Queued;
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public class MediaCaptionJobItemModel : BaseEntity
{
    public Guid JobId { get; set; }
    public Guid MediaAssetId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public MediaCaptionJobItemStatus Status { get; set; } = MediaCaptionJobItemStatus.Pending;
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
