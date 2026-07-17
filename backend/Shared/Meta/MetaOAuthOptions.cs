namespace Backend.Shared.Meta;

public class MetaOAuthOptions
{
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com";
    public string GraphVersion { get; set; } = "v20.0";
    public string RedirectUri { get; set; } = string.Empty;
    public string FrontendSuccessUri { get; set; } = "http://localhost:5173/platforms?metaConnected=success";
    public string FrontendErrorUri { get; set; } = "http://localhost:5173/platforms?metaConnected=error";
    // Empty default on purpose: scopes come SOLELY from config (appsettings MetaOAuth:Scopes).
    // A non-empty default is MERGED (not replaced) by the .NET config binder, which would
    // silently re-add any scope removed from appsettings.
    public List<string> Scopes { get; set; } = [];
}
