namespace Backend.Shared.SocialPublish;

public class SocialPublishOptions
{
    public string DefaultProvider { get; set; } = "facebook";
    public bool UseRealFacebook { get; set; }
    public FacebookPublishOptions Facebook { get; set; } = new();
}

public class FacebookPublishOptions
{
    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com";
    public string GraphVersion { get; set; } = "v20.0";
    public int TimeoutSeconds { get; set; } = 30;
}
