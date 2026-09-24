namespace Backend.Shared.Email;

/// <summary>
/// Cấu hình SMTP để gửi email (bản tin trang tin, sau này có thể dùng chung cho việc khác).
/// Password để RỖNG trong appsettings.json — nạp qua appsettings.Production.json (gitignored)
/// hoặc biến môi trường, giống Telegram:BotToken.
/// </summary>
public class EmailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool EnableSsl { get; set; } = true;
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "VNI Education";

    /// <summary>
    /// Gốc URL công khai của CHÍNH BACKEND này, vd "https://auto.vni.edu.vn" — KHÔNG kèm dấu /
    /// cuối. Dùng để dựng link huỷ đăng ký trong email
    /// (<c>{PublicApiBaseUrl}/api/news-public/unsubscribe?token=...</c>) — khác
    /// NewsSite:PublicBaseUrl vốn là domain trang tin, không phải domain backend.
    /// </summary>
    public string PublicApiBaseUrl { get; set; } = "";
}
