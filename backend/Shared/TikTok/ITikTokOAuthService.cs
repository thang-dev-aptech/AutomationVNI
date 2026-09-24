namespace Backend.Shared.TikTok;

public interface ITikTokOAuthService
{
    bool IsConfigured();

    /// <summary>Vấn đề cấu hình cụ thể (thiếu ClientKey/ClientSecret/RedirectUri/Scopes), hoặc null nếu đã sẵn sàng.</summary>
    string? DescribeConfigIssue();

    string BuildConnectUrl(Guid userId, string userName);

    Task<TikTokOAuthCallbackResult> HandleCallbackAsync(string code, string state, CancellationToken ct = default);

    /// <summary>
    /// Refresh bằng refresh_token (không phải access_token như Threads). TikTok rotate cả 2 token
    /// mỗi lần refresh — trả về null khi TikTok từ chối, lúc đó user phải kết nối lại.
    /// </summary>
    Task<TikTokTokenResult?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);
}
