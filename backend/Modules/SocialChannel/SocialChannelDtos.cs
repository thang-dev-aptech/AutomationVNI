using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;

namespace Backend.Modules.SocialChannel;

public class CreateSocialChannelRequest
{
    public SocialPlatform Platform { get; set; }
    public SocialChannelType ChannelType { get; set; } = SocialChannelType.Page;
    public string PageName { get; set; } = string.Empty;
    public string ExternalPageId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public Guid? SocialConnectionId { get; set; }
}

public class UpdateSocialChannelRequest
{
    public string? PageName { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public bool? IsActive { get; set; }
}

public class SocialChannelFilterRequest : PagedFilterRequest
{
    public SocialPlatform? Platform { get; set; }
    public SocialChannelType? ChannelType { get; set; }
    public Guid? SocialConnectionId { get; set; }
    public bool? IsActive { get; set; }
}

public class SocialChannelResponse
{
    public Guid Id { get; set; }
    public SocialPlatform Platform { get; set; }
    public SocialChannelType ChannelType { get; set; }
    public string PageName { get; set; } = string.Empty;
    public string ExternalPageId { get; set; } = string.Empty;
    public Guid? SocialConnectionId { get; set; }
    public string? ExtraJson { get; set; }
    public DateTime? TokenExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
