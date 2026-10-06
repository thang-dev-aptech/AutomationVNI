using Backend.Shared;

namespace Backend.Modules.ChannelGroup;

/// <summary>
/// Thành viên nhóm kênh. Cặp (ChannelGroupId, SocialChannelId) unique khi chưa xoá.
/// Không FK constraint — validate tồn tại kênh trong repository.
/// </summary>
public class ChannelGroupMemberModel : BaseEntity
{
    public Guid ChannelGroupId { get; set; }
    public Guid SocialChannelId { get; set; }
}
