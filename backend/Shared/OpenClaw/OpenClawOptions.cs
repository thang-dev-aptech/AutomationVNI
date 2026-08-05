namespace Backend.Shared.OpenClaw;

/// <summary>
/// Cấu hình gọi browser control của OpenClaw.
///
/// LƯU Ý VẬN HÀNH: HTTP API này chỉ bật khi service gateway có biến
/// OPENCLAW_EAGER_BROWSER_CONTROL_SERVER=1 (đặt trong
/// ~/.openclaw/service-env/ai.openclaw.gateway.env). File đó do OpenClaw sinh ra và có
/// dòng cảnh báo "Do not edit while the gateway service is installed" — chạy lại
/// `openclaw gateway install --force` có thể xoá mất biến, và khi đó cào sẽ chết.
/// Vì vậy OpenClawBrowserClient phải kiểm tra lúc gọi và báo lỗi rõ ràng, không hỏng âm thầm.
///
/// Cổng browser control = cổng gateway + 2 (đo thực tế: gateway 18789 → browser 18791).
/// Tài liệu không ghi cách suy ra nên để cấu hình thẳng, đừng tự tính.
/// </summary>
public class OpenClawOptions
{
    public bool Enabled { get; set; }
    /// <summary>Browser control HTTP API. KHÔNG phải cổng gateway (18789).</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:18791";
    /// <summary>Token gateway, lấy ở ~/.openclaw/openclaw.json → gateway.auth.token.</summary>
    public string Token { get; set; } = string.Empty;
    public int NavigateTimeoutSeconds { get; set; } = 45;
    public int EvaluateTimeoutSeconds { get; set; } = 60;
    /// <summary>Nghỉ giữa hai trang bài — vừa lịch sự với báo, vừa đỡ bị chặn.</summary>
    public int DelayBetweenPagesMs { get; set; } = 1500;
}
