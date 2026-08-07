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

    /// <summary>Ví dụ https://tintuc.vni.edu.vn — KHÔNG kèm dấu / ở cuối.</summary>
    public string PublicBaseUrl { get; set; } = "";

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
}
