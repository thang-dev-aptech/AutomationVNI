namespace Backend.Modules.SocialChannel.Enums;

public enum SocialPlatform
{
    Facebook  = 1,
    LinkedIn  = 2,
    Instagram = 3,
    TikTok    = 4
}

/// <summary>Subtype of a social channel under a connection (Page / IG / Group).</summary>
public enum SocialChannelType
{
    Page = 1,
    Instagram = 2,
    Group = 3
}

/// <summary>OAuth provider for SocialConnection (Meta, LinkedIn, …).</summary>
public enum SocialProvider
{
    Meta = 1,
    LinkedIn = 2,
    Threads = 3
}
