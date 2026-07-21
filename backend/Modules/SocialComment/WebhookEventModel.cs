using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment.Enums;
using Backend.Shared;

namespace Backend.Modules.SocialComment;

public class WebhookEventModel : BaseEntity
{
    public SocialPlatform Platform { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string? ObjectId { get; set; }
    public string? Verb { get; set; }
    public string? Item { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public WebhookEventStatus Status { get; set; } = WebhookEventStatus.Pending;
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
