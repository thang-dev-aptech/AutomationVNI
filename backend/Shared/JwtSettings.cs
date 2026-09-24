namespace Backend.Shared;

public class JwtSettings
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
}

public class SeedSettings
{
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>
    /// Tài khoản demo/reviewer role Viewer (chỉ xem, không tạo/sửa/xoá/đăng được) — dùng để đưa
    /// cho bên thứ 3 audit (vd. TikTok App Review yêu cầu test account trong "Apply Reason") mà
    /// không phải chia sẻ tài khoản Admin thật. Để trống thì bỏ qua seed, không lỗi.
    /// </summary>
    public string ReviewerEmail { get; set; } = string.Empty;
    public string ReviewerPassword { get; set; } = string.Empty;
}
