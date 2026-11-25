using Backend.Shared;

namespace Backend.Modules.Campaign.FullMedia;

public class CampaignMediaEvaluationModel : BaseEntity
{
    public Guid CampaignId { get; set; }
    public Guid MediaAssetId { get; set; }
    public CampaignMediaEvaluationStatus Status { get; set; } = CampaignMediaEvaluationStatus.Pending;
    public string? ReasonCode { get; set; }
    public double? BestScore { get; set; }
    public Guid? BestChannelId { get; set; }
    public int Attempts { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? EvaluatedAt { get; set; }
    public string? InputHash { get; set; }
    public decimal CostUsd { get; set; }
}
