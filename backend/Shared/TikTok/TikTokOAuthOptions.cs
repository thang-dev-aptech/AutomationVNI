namespace Backend.Shared.TikTok;

public class TikTokOAuthOptions
{
    public string ClientKey { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Domain user được redirect tới để đăng nhập/cấp quyền (dialog authorize).</summary>
    public string AuthBaseUrl { get; set; } = "https://www.tiktok.com";

    /// <summary>Domain API server-to-server: đổi code lấy token, lấy profile, refresh token.</summary>
    public string ApiBaseUrl { get; set; } = "https://open.tiktokapis.com";

    public string ApiVersion { get; set; } = "v2";

    public string RedirectUri { get; set; } = string.Empty;

    public string FrontendSuccessUri { get; set; } = "http://localhost:5173/platforms?tiktokConnected=success";
    public string FrontendErrorUri { get; set; } = "http://localhost:5173/platforms?tiktokConnected=error";

    // Rỗng có chủ đích: .NET config binder MERGE list thay vì replace, nên scope phải
    // đến hoàn toàn từ appsettings/user-secrets (giống lý do trong MetaOAuthOptions.Scopes).
    public List<string> Scopes { get; set; } = [];

    /// <summary>
    /// Access token TikTok chỉ sống ~24h (khác Threads 60 ngày) — đơn vị refresh phải tính bằng
    /// giờ chứ không phải ngày, nếu không worker sẽ luôn refresh trễ hơn thời điểm hết hạn.
    /// </summary>
    public int RefreshBeforeExpiryHours { get; set; } = 4;

    public bool RefreshEnabled { get; set; } = true;

    /// <summary>Chu kỳ worker quét token sắp hết hạn. Token sống ngắn (24h) nên quét dày hơn Threads.</summary>
    public int RefreshIntervalMinutes { get; set; } = 60;
}
