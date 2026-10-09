namespace Backend.Modules.Post;

/// <summary>
/// DTO riêng cho POST api/Post/bulk-action (R-033).
/// Tách file để không đụng PostDtos.cs (ownership task t2).
/// </summary>
public class PostBulkActionRequest
{
    /// <summary>cancelSchedule | publishNow | delete</summary>
    public string Action { get; set; } = string.Empty;

    public List<Guid> PostIds { get; set; } = [];
}

public class PostBulkActionResponse
{
    public List<PostBulkActionItemResult> Results { get; set; } = [];
}

public class PostBulkActionItemResult
{
    public Guid PostId { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? ErrorCode { get; set; }
}

public static class PostBulkActions
{
    public const string CancelSchedule = "cancelSchedule";
    public const string PublishNow = "publishNow";
    public const string Delete = "delete";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        CancelSchedule,
        PublishNow,
        Delete
    };
}
