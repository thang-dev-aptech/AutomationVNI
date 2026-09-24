namespace Backend.Shared.TikTok;

public class TikTokConnectUrlResponse
{
    public string Url { get; set; } = string.Empty;
    public string? Hint { get; set; }
}

public class TikTokOAuthCallbackResult
{
    public Guid? SocialConnectionId { get; set; }

    /// <summary>Tài khoản TikTok đã sync. Luôn 0 hoặc 1 — một lần authorize ra đúng 1 tài khoản.</summary>
    public int ProfilesSynced { get; set; }

    /// <summary>Channel bị soft-delete vì không còn khớp lần sync này (đổi tài khoản khác).</summary>
    public int ChannelsRemoved { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>Danh sách scope (đã cấp) dạng comma-separated.</summary>
    public string? GrantedScopes { get; set; }

    public DateTime? TokenExpiresAt { get; set; }
}

public class TikTokUserProfileDto
{
    public string OpenId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }

    /// <summary>Nhãn hiển thị: tên hiển thị nếu có, else fallback về open_id.</summary>
    public string Label =>
        !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName.Trim() : OpenId;
}

/// <summary>Kết quả đổi code lấy token — cả access lẫn refresh token đều có hạn và cùng hết hạn một lượt khi rotate.</summary>
public class TikTokTokenResult
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime RefreshTokenExpiresAt { get; set; }
    public string OpenId { get; set; } = string.Empty;
    public string? Scope { get; set; }
}

internal class TikTokOAuthStateEntry
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
}
