using Backend.Shared;

namespace Backend.Modules.ShortLink;

/// <summary>
/// Link rút gọn nội bộ. Sinh ra vì bài đăng nền màu của Facebook chỉ cho 130 ký tự, mà URL báo
/// Việt Nam trung bình đã 111 ký tự (đo trên kho thật) — dán nguyên vào là hết sạch chỗ viết.
/// </summary>
public class ShortLinkModel : BaseEntity
{
    /// <summary>Mã ngắn trên URL, vd "a1b2c3". Không dấu, phân biệt hoa thường.</summary>
    public string Code { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public int ClickCount { get; set; }
    public DateTime? LastClickedAt { get; set; }
    /// <summary>Bài nào dùng link này — để soi lại khi cần. No FK.</summary>
    public Guid? PostId { get; set; }
}
