namespace Backend.Shared.Meta;

public interface IMetaOAuthService
{
    bool IsConfigured();

    string BuildConnectUrl(Guid userId, string userName);

    Task<MetaOAuthCallbackResult> HandleCallbackAsync(string code, string state, CancellationToken ct = default);
}
