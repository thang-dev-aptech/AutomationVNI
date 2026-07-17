using Backend.Modules.PublishLog.Enums;
using Backend.Shared;

namespace Backend.Modules.PublishLog;

public class CreatePublishLogRequest
{
    public Guid PostId { get; set; }
    public Guid SocialChannelId { get; set; }
    public int AttemptNumber { get; set; }
    public PublishStatus Status { get; set; }
    public string? ExternalPostId { get; set; }
    public string? PublishedUrl { get; set; }
    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class UpdatePublishLogRequest
{
    public PublishStatus? Status { get; set; }
    public string? ExternalPostId { get; set; }
    public string? PublishedUrl { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? ResponsePayload { get; set; }
}

public class PublishLogFilterRequest : PagedFilterRequest
{
    public Guid? PostId { get; set; }
    public Guid? SocialChannelId { get; set; }
    public PublishStatus? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class PublishLogResponse
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid SocialChannelId { get; set; }
    public int AttemptNumber { get; set; }
    public PublishStatus Status { get; set; }
    public string? ExternalPostId { get; set; }
    public string? PublishedUrl { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponsePayload { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }
}
