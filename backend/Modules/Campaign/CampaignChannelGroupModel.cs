using Backend.Shared;

namespace Backend.Modules.Campaign;

/// <summary>Nhóm kênh gắn chiến dịch. Unique (CampaignId, ChannelGroupId) khi chưa xoá.</summary>
public class CampaignChannelGroupModel : BaseEntity
{
    public Guid CampaignId { get; set; }
    public Guid ChannelGroupId { get; set; }
}
