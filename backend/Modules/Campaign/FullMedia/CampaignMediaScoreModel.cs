using Backend.Shared;

namespace Backend.Modules.Campaign.FullMedia;

public class CampaignMediaScoreModel : BaseEntity
{
    public Guid CampaignId { get; set; }
    public Guid MediaAssetId { get; set; }
    public Guid SocialChannelId { get; set; }
    /// <summary>1 = Vision A, 2 = Vision B.</summary>
    public int Pass { get; set; }
    public double Score { get; set; }
    public bool Passed { get; set; }
    public DateTime ScoredAt { get; set; }
}
