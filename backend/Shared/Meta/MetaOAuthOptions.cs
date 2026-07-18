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

    /// <summary>
    /// Facebook Login for Business configuration ID.
    /// Required when the Meta app uses "Facebook Login for Business" (otherwise OAuth shows
    /// "content isn't available" / app unavailable after login).
    /// Create at: Meta App → Facebook Login for Business → Configurations.
    /// </summary>
    public string ConfigId { get; set; } = string.Empty;

    /// <summary>
    /// When true, Connect Meta first hits Facebook logout then the OAuth dialog.
    /// Default false: Facebook often ignores logout.php?next= and lands on facebook.com home,
    /// so the user never reaches /api/meta/callback. Prefer logging out FB manually if needed.
    /// </summary>
    public bool ForceReLogin { get; set; } = false;

    // Empty default on purpose: scopes come SOLELY from config (appsettings MetaOAuth:Scopes).
    // A non-empty default is MERGED (not replaced) by the .NET config binder, which would
    // silently re-add any scope removed from appsettings.
    // When ConfigId is set, Meta uses the configuration's permissions — Scopes are optional.
    public List<string> Scopes { get; set; } = [];
}
