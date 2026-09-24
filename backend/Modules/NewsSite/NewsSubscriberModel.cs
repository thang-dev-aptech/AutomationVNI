using Backend.Shared;

namespace Backend.Modules.NewsSite;

/// <summary>
/// Email đăng ký nhận tin từ trang tin công khai. Đăng ký là nhận ngay — không xác nhận email —
/// mỗi thư gửi đi đều kèm link huỷ đăng ký riêng theo <see cref="UnsubscribeToken"/>.
/// </summary>
public class NewsSubscriberModel : BaseEntity
{
    public string Email { get; set; } = "";

    /// <summary>Đang nhận tin hay không — chuyển false khi bấm huỷ đăng ký, không xoá dòng.</summary>
    public bool IsActive { get; set; } = true;

    public string UnsubscribeToken { get; set; } = "";

    public DateTime? UnsubscribedAt { get; set; }
}
