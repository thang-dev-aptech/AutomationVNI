namespace Backend.Shared.Meta;

public interface IMetaOAuthService
{
    bool IsConfigured();

    /// <summary>Specific config problem (missing AppId/AppSecret/RedirectUri), or null when ready.</summary>
    string? DescribeConfigIssue();

    string BuildConnectUrl(Guid userId, string userName);

    Task<MetaOAuthCallbackResult> HandleCallbackAsync(string code, string state, CancellationToken ct = default);
}
