using Backend.Shared;

namespace Backend.Modules.Campaign;

/// <summary>Kênh lẻ gắn chiến dịch. Unique (CampaignId, SocialChannelId) khi chưa xoá.</summary>
public class CampaignChannelModel : BaseEntity
{
    public Guid CampaignId { get; set; }
    public Guid SocialChannelId { get; set; }
}
