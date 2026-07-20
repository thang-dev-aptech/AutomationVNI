using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;

namespace Backend.Modules.SocialConnection;

public class SocialConnectionResponse
{
    public Guid Id { get; set; }
    public SocialProvider Provider { get; set; }
    public string ExternalUserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public bool IsActive { get; set; }
    public int PageCount { get; set; }
    public int InstagramCount { get; set; }
    public int GroupCount { get; set; }
    public int ThreadsCount { get; set; }
    public List<SocialChannelResponse> Channels { get; set; } = [];
}

public class SocialConnectionFilterRequest : PagedFilterRequest
{
    public SocialProvider? Provider { get; set; }
    public bool? IsActive { get; set; }
}
