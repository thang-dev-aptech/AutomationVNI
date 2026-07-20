namespace Backend.Shared.Threads;

public class ThreadsOAuthOptions
{
    /// <summary>
    /// Threads App ID. A Meta app created with the Threads use case issues TWO id/secret pairs —
    /// this must be the Threads-specific one (App Dashboard → Threads → Settings), not the Facebook App ID.
    /// </summary>
    public string AppId { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Threads runs on its own host — not graph.facebook.com.</summary>
    public string GraphBaseUrl { get; set; } = "https://graph.threads.net";
    public string GraphVersion { get; set; } = "v1.0";

    /// <summary>Authorization dialog host. Unversioned, and distinct from GraphBaseUrl.</summary>
    public string AuthorizeUrl { get; set; } = "https://threads.net/oauth/authorize";

    /// <summary>Must match a Redirect Callback URL registered under Threads → Settings, character for character.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    public string FrontendSuccessUri { get; set; } = "http://localhost:5173/platforms?threadsConnected=success";
    public string FrontendErrorUri { get; set; } = "http://localhost:5173/platforms?threadsConnected=error";

    // Empty default on purpose — same reason as MetaOAuthOptions.Scopes: a non-empty default is
    // MERGED (not replaced) by the config binder, silently re-adding scopes removed from appsettings.
    public List<string> Scopes { get; set; } = [];

    /// <summary>Refresh long-lived tokens this many days before expiry. Threads tokens die permanently at 60 days.</summary>
    public int RefreshBeforeExpiryDays { get; set; } = 7;

    public bool RefreshEnabled { get; set; } = true;

    /// <summary>How often the refresh worker sweeps for near-expiry tokens.</summary>
    public int RefreshIntervalHours { get; set; } = 6;
}
