namespace Backend.Shared.Threads;

public interface IThreadsOAuthService
{
    bool IsConfigured();

    /// <summary>Specific config problem (missing AppId/AppSecret/RedirectUri/Scopes), or null when ready.</summary>
    string? DescribeConfigIssue();

    string BuildConnectUrl(Guid userId, string userName);

    Task<ThreadsOAuthCallbackResult> HandleCallbackAsync(string code, string state, CancellationToken ct = default);

    /// <summary>
    /// Refresh a long-lived token (grant_type=th_refresh_token). Returns null when Threads rejects it —
    /// the token is then unrecoverable and the user must reconnect.
    /// </summary>
    Task<(string Token, DateTime ExpiresAt)?> RefreshLongLivedTokenAsync(string token, CancellationToken ct = default);
}
