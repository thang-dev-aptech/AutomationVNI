using Backend.Shared;

namespace Backend.Modules.ChannelGroup;

/// <summary>Nhóm kênh dùng chung toàn hệ thống (CHANNEL-GROUP-01). Không FK constraint.</summary>
public class ChannelGroupModel : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
