namespace Backend.Shared.Meta;

public class MetaConnectUrlResponse
{
    public string Url { get; set; } = string.Empty;

    /// <summary>classic = scope-based Facebook Login; business = config_id Login for Business.</summary>
    public string Mode { get; set; } = "classic";

    /// <summary>Optional setup hint when ConfigId is missing on a Business-style Pages connect.</summary>
    public string? Hint { get; set; }
}

public class MetaOAuthCallbackResult
{
    public Guid? SocialConnectionId { get; set; }
    public int FacebookPagesSynced { get; set; }
    public int InstagramAccountsSynced { get; set; }
    public int FacebookGroupsSynced { get; set; }
}

public class MetaUserProfileDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PictureUrl { get; set; }
}

public class MetaPageAccountDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public MetaInstagramBusinessDto? InstagramBusinessAccount { get; set; }
}

public class MetaInstagramBusinessDto
{
    public string Id { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Name { get; set; }
}

public class MetaGroupDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Privacy { get; set; }
    public bool? Administrator { get; set; }
    /// <summary>User token used when group-specific token is unavailable.</summary>
    public string? AccessToken { get; set; }
}

internal class MetaOAuthStateEntry
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
}
