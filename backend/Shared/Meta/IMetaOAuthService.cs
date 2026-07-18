namespace Backend.Shared.Meta;

public interface IMetaOAuthService
{
    bool IsConfigured();

    /// <summary>Specific config problem (missing AppId/AppSecret/RedirectUri), or null when ready.</summary>
    string? DescribeConfigIssue();

    /// <summary>
    /// Live preflight: verifies Meta actually recognizes the configured App ID + Secret (client_credentials).
    /// Returns an actionable message when Meta rejects them (e.g. invalid/deleted App ID → Graph code 101),
    /// or null when ready. Best-effort — transient network errors return null so a healthy setup is never blocked.
    /// </summary>
    Task<string?> DescribeLiveConfigIssueAsync(CancellationToken ct = default);

    string BuildConnectUrl(Guid userId, string userName);

    Task<MetaOAuthCallbackResult> HandleCallbackAsync(string code, string state, CancellationToken ct = default);
}
