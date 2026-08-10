namespace Backend.Modules.NewsSite;

/// <summary>
/// Cấu hình trang tin công khai.
///
/// <see cref="PublicBaseUrl"/> là thứ dễ quên nhất khi lên production và hỏng thì hỏng câm:
/// link dán vào bài Facebook dựng từ đây; để rỗng thì ra đường dẫn tương đối, bấm từ Facebook
/// là 404. Vì vậy nơi dựng link phải trả null khi thiếu, không ghép bừa.
/// </summary>
public class NewsSiteOptions
{
    /// <summary>Bật bộ sinh trang tĩnh. Tắt thì luồng duyệt vẫn chạy, chỉ không xuất web.</summary>
    public bool Enabled { get; set; }

    /// <summary>Cho AI viết bài hoàn chỉnh khi duyệt tin.</summary>
    public bool ComposeEnabled { get; set; } = true;

    /// <summary>
    /// Bài 800 từ mất khá lâu. Đo trên gateway vietai: chấm điểm một bài (ngắn hơn nhiều) đã
    /// mất ~28 giây, nên 180 giây ở đây là mức phải chăng. Để chật thì bài đứt giữa chừng.
    /// </summary>
    public int ComposeTimeoutSeconds { get; set; } = 180;

    /// <summary>Quá số lần này mà vẫn không viết được thì bỏ, kèm ghi chú cho người kiểm tra.</summary>
    public int MaxComposeAttempts { get; set; } = 3;

    /// <summary>
    /// Địa chỉ CÔNG KHAI của trang tin, ví dụ https://tintuc.vni.edu.vn — KHÔNG kèm dấu / cuối.
    ///
    /// Đây là địa chỉ ĐI RA NGOÀI: canonical, og:url, sitemap, và link dán ở bình luận Facebook.
    /// Giữ đúng tên miền thật kể cả khi chưa trỏ DNS — thẻ og trong HTML phải khớp địa chỉ mà
    /// độc giả sẽ mở, không phải địa chỉ máy dev.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>
    /// Địa chỉ XEM THỬ, chỉ dùng cho nút "Xem trên web" trong giao diện quản trị.
    ///
    /// Tách khỏi PublicBaseUrl vì hai thứ này khác nhau khi chưa gắn tên miền: người duyệt cần
    /// mở được bài NGAY để kiểm, còn thẻ og và link Facebook thì phải mang tên miền thật.
    /// Dùng chung một giá trị thì hoặc nút xem thử dẫn vào tên miền chết, hoặc bài Facebook
    /// dẫn về localhost.
    ///
    /// Bỏ trống = dùng PublicBaseUrl. Gắn tên miền xong thì xoá dòng này trong cấu hình.
    /// </summary>
    public string PreviewBaseUrl { get; set; } = "";

    /// <summary>Thư mục nginx phục vụ. Trong Docker phải bind mount, xem README của VNINews.</summary>
    public string OutputPath { get; set; } = "Storage/News";

    /// <summary>Khuôn HTML sao từ VNINews/ vào lúc build.</summary>
    public string TemplatePath { get; set; } = "Templates/news";

    public string SiteName { get; set; } = "Tin tức VNI Education";
    public string AuthorName { get; set; } = "VNI Education";

    /// <summary>Số bài trên trang chủ.</summary>
    public int HomePageSize { get; set; } = 12;

    /// <summary>
    /// Chống trùng ở khâu xuất bản. Tắt thì mọi bài đều lên, kể cả bài y hệt bài đã có —
    /// để lùi nhanh nếu bộ này chặn nhầm quá nhiều.
    /// </summary>
    public bool PublishDedupEnabled { get; set; } = true;

    /// <summary>
    /// Chồng lấp tít+sapo từ mức này trở lên là chặn thẳng, không hỏi AI.
    ///
    /// 0,70 chứ không phải 0,80: hai bài đang nằm trên trang chủ đo được 0,688 — sát dưới
    /// 0,70 — nên đây đã là mức chặt. Hạ tiếp thì bắt đầu chặn nhầm tin tiếp diễn thật.
    /// </summary>
    public double PublishDuplicateMin { get; set; } = 0.70;

    /// <summary>Dưới mức này thì cho đăng luôn, khỏi tốn lượt gọi AI.</summary>
    public double PublishBorderlineMin { get; set; } = 0.45;

    /// <summary>
    /// Hết giờ thì CHO ĐĂNG chứ không chặn — xem lý do trong NewsDedupService.
    /// 25 giây theo đúng khuôn đã chỉnh cho AiJudge ở luồng cào.
    /// </summary>
    public int PublishDedupTimeoutSeconds { get; set; } = 25;

    /// <summary>
    /// Tối đa bao nhiêu bài mỗi page mỗi ngày.
    ///
    /// Hiện có 15 page và 6 bài web: bấm đăng tất cho tất là 90 bài fanpage trong một buổi.
    /// Facebook không báo lỗi gì — nó chỉ giảm phân phối, và người theo dõi bỏ theo dõi. Cả
    /// hai đều không hiện ra ở bất cứ đâu trong hệ thống này.
    ///
    /// 2 bài/ngày khớp nhịp thật: 19/37 tin đạt trên 80 điểm, tức khoảng 1–2 bài đáng đăng mỗi
    /// ngày. Đặt 0 để bỏ hạn mức.
    /// </summary>
    public int MaxFanpagePostsPerChannelPerDay { get; set; } = 2;

    /// <summary>
    /// Từ điểm này trở lên thì giao diện gắn nhãn "nên đăng fanpage".
    ///
    /// Chỉ GỢI Ý, không chặn — người duyệt vẫn đăng được bài điểm thấp hơn nếu muốn. Lấy 85 vì
    /// phân bố điểm tách đôi rõ rệt: 19 tin ở vùng 80–100, trung bình 90,5.
    /// </summary>
    public int FanpageSuggestMinScore { get; set; } = 85;
}
