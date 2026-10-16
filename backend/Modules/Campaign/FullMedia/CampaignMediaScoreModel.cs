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

    /// <summary>
    /// Chi phí ước tính của lượt chấm (gán vào một dòng mỗi pass) — dùng Sum theo ScoredAt ngày UTC.
    /// Cộng dồn khi chấm lại cùng dòng.
    /// </summary>
    public decimal CostUsd { get; set; }

    /// <summary>
    /// PageProfile.Hash tại thời điểm chấm. NULL = dòng cũ / chưa biết → chấm lại khi rescore.
    /// </summary>
    public string? ProfileHash { get; set; }
}
