using Backend.Shared;

namespace Backend.Modules.PageMetrics;

/// <summary>
/// Ảnh chụp chỉ số của một page vào một NGÀY. Mỗi page mỗi ngày đúng một dòng.
///
/// ═══ VÌ SAO PHẢI CÓ BẢNG NÀY, KHÔNG GỌI THẲNG API MỖI LẦN MỞ DASHBOARD ═══
///
/// Facebook chỉ trả về TRẠNG THÁI HIỆN TẠI: page đang có bao nhiêu người theo dõi, bài đang có
/// bao nhiêu like. Không có đường nào hỏi "tuần trước bao nhiêu". Không tự ghi lại thì vĩnh viễn
/// không vẽ được xu hướng — mà "tháng này hơn tháng trước bao nhiêu" mới là thứ khách hỏi đầu
/// tiên, chứ không phải con số tuyệt đối.
///
/// Đo thật: một lượt đồng bộ 15 page đang bật mất 75 giây và khoảng 75 lượt gọi Graph API — gần
/// hết thời gian là chờ mạng. Gọi thẳng mỗi lần mở dashboard thì vừa không dùng nổi, vừa đốt hạn
/// mức API cho một con số mà cả ngày không đổi mấy.
///
/// KHÔNG xoá dòng cũ. Bảng này mỗi ngày thêm 18 dòng — một năm khoảng 6.600 dòng, vài trăm KB.
/// Rẻ hơn nhiều so với việc mất lịch sử.
/// </summary>
public class ChannelMetricDailyModel : BaseEntity
{
    public Guid SocialChannelId { get; set; }

    /// <summary>Ngày theo GIỜ VIỆT NAM, phần giờ luôn bằng 0.</summary>
    /// <remarks>
    /// Cố ý dùng giờ Việt Nam chứ không phải UTC. Chốt ngày theo UTC thì lượt đồng bộ 7 giờ sáng
    /// giờ Việt bị ghi vào ngày HÔM TRƯỚC, và khách nhìn báo cáo thấy lệch một ngày so với
    /// Facebook — lỗi rất khó giải thích mà cũng rất khó tin là mình đúng.
    /// </remarks>
    public DateTime Date { get; set; }

    /// <summary>Người theo dõi page tại thời điểm chụp.</summary>
    public int Followers { get; set; }

    /// <summary>Số bài page đăng trong ngày đó.</summary>
    public int PostsPublished { get; set; }

    // Tổng tương tác CỘNG DỒN trên toàn bộ bài đã đồng bộ, tại thời điểm chụp — không phải
    // phát sinh riêng trong ngày. Lấy hiệu hai ngày liên tiếp là ra phát sinh; làm ngược lại
    // thì không bao giờ dựng lại được tổng.
    public int TotalLikes { get; set; }
    public int TotalComments { get; set; }
    public int TotalShares { get; set; }

    /// <summary>Lượt đồng bộ này có lỗi gì không — để dashboard không im lặng báo số cũ.</summary>
    public string? SyncError { get; set; }
}
