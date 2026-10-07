using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;

namespace Backend.Modules.ChannelGroup;

public class CreateChannelGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<Guid>? ChannelIds { get; set; }
}

public class UpdateChannelGroupRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    /// <summary>Null = không đổi danh sách kênh; list (kể cả rỗng) = thay thế toàn bộ.</summary>
    public List<Guid>? ChannelIds { get; set; }
}

public class ChannelGroupFilterRequest : PagedFilterRequest
{
}

public class ChannelGroupChannelResponse
{
    public Guid Id { get; set; }
    public string PageName { get; set; } = string.Empty;
    public SocialPlatform Platform { get; set; }
    public SocialChannelType ChannelType { get; set; }
    public string ExternalPageId { get; set; } = string.Empty;
}

public class ChannelGroupResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int ChannelCount { get; set; }
    public List<ChannelGroupChannelResponse> Channels { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}
