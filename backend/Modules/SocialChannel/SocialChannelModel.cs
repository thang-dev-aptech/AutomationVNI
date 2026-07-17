using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;

namespace Backend.Modules.SocialChannel;

public class SocialChannelModel : BaseEntity
{
    public SocialPlatform Platform { get; set; }
    public SocialChannelType ChannelType { get; set; } = SocialChannelType.Page;
    public string PageName { get; set; } = string.Empty;
    public string ExternalPageId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? SocialConnectionId { get; set; }
}
