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
    public List<string> Scopes { get; set; } =
    [
        // Minimal Pages sync first — add IG/Groups scopes after Connect works.
        "public_profile",
        "pages_show_list",
        "pages_read_engagement",
        "pages_manage_posts"
    ];
}
