namespace Backend.Shared.Threads;

public class ThreadsConnectUrlResponse
{
    public string Url { get; set; } = string.Empty;
    public string? Hint { get; set; }
}

public class ThreadsOAuthCallbackResult
{
    public Guid? SocialConnectionId { get; set; }

    /// <summary>Threads profiles synced. Always 0 or 1 — one authorization grants one profile.</summary>
    public int ProfilesSynced { get; set; }

    /// <summary>Channels soft-deleted because they disappeared from Threads on this sync.</summary>
    public int ChannelsRemoved { get; set; }

    public string? Username { get; set; }

    /// <summary>Comma-separated scopes requested for this grant.</summary>
    public string? GrantedScopes { get; set; }

    public DateTime? TokenExpiresAt { get; set; }
}

public class ThreadsUserProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Name { get; set; }
    public string? PictureUrl { get; set; }

    /// <summary>Display label: "@username" when available, else name, else the raw id.</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Username) ? $"@{Username.Trim()}"
        : !string.IsNullOrWhiteSpace(Name) ? Name.Trim()
        : Id;
}

internal class ThreadsOAuthStateEntry
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
}
