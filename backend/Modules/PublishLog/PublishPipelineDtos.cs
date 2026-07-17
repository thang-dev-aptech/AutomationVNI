using Backend.Modules.PublishLog.Enums;

namespace Backend.Modules.PublishLog;

public class FailPublishLogRequest
{
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public class ProcessPublishLogResponse
{
    public Guid PublishLogId { get; set; }
    public Guid PostId { get; set; }
    public PublishStatus Status { get; set; }
    public string? ExternalPostId { get; set; }
    public string? PublishedUrl { get; set; }
    public string? ResponsePayload { get; set; }
}

public class ProcessDueScheduledResult
{
    public int Picked { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<ProcessDueScheduledItem> Items { get; set; } = [];
}

public class ProcessDueScheduledItem
{
    public Guid PostId { get; set; }
    public Guid? PublishLogId { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? PublishedUrl { get; set; }
}

public static class PublishIdempotency
{
    public static string BuildKey(Guid postId, Guid socialChannelId, DateTime scheduledAt)
        => $"publish:{postId}:{socialChannelId}:{scheduledAt:yyyyMMddHHmmss}";
}

public static class MockPublishResult
{
    public static (string ExternalPostId, string PublishedUrl, string ResponseJson) Create(Guid publishLogId)
    {
        var externalId = $"mock_{Guid.NewGuid():N}";
        var publishedUrl = $"https://facebook.com/mock/posts/{publishLogId:N}";
        var responseJson = $$"""
            {"externalPostId":"{{externalId}}","publishedUrl":"{{publishedUrl}}","platform":"facebook","mock":true}
            """;
        return (externalId, publishedUrl, responseJson);
    }
}
