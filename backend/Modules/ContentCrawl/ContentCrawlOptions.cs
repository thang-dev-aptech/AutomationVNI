namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Cấu hình worker cào tin. Mặc định Enabled=false giống Scheduler/GenerationWorker —
/// bật riêng ở appsettings.Development.json và biến môi trường production.
/// </summary>
public class ContentCrawlOptions
{
    public bool Enabled { get; set; }
    public int IntervalSeconds { get; set; } = 300;
    /// <summary>Số nguồn xử lý mỗi tick — chặn việc 7 feed cùng tới hạn rồi nổ một lượt.</summary>
    public int MaxSourcesPerTick { get; set; } = 5;
    public int MaxConcurrency { get; set; } = 2;
    public int FetchTimeoutSeconds { get; set; } = 25;
    public string UserAgent { get; set; } = "VNIAutomationBot/1.0 (+https://vni.edu.vn)";

    public int DefaultLookbackHours { get; set; } = 48;
    public int DefaultMaxItemsPerRun { get; set; } = 30;
    /// <summary>
    /// Toàn văn ngắn hơn mức này thì coi như bóc hỏng → đánh Filtered, không đưa cho AI.
    /// Gặp thật: trang chuyên mục video (dantri.com.vn/dt360/...) không có bài chữ nào, bóc ra
    /// đúng 54 ký tự là chuỗi trợ năng của trình phát video.
    /// </summary>
    public int MinContentLength { get; set; } = 300;

    /// <summary>
    /// Dùng cho tin CHƯA lấy được toàn văn (nguồn feed, trang chặn). Đo trên tiêu đề + tóm tắt.
    /// 80 ký tự: tóm tắt RSS đo trên 1.480 bài dài trung bình 171 ký tự, nên 80 chỉ loại những
    /// mục thật sự rỗng chứ không loại oan tin có tóm tắt ngắn.
    /// </summary>
    public int MinSummaryLength { get; set; } = 80;

    /// <summary>Số bài chấm trùng / xào nháp mỗi tick.</summary>
    public int ProcessBatchSize { get; set; } = 10;
    /// <summary>Quá số lần này mà vẫn lỗi thì đẩy sang Pending kèm ghi chú, không lặp nữa.</summary>
    public int MaxProcessAttempts { get; set; } = 3;

    /// <summary>
    /// Ảnh thumbnail thuộc bản quyền toà soạn. Mặc định CHỈ lưu URL cho người duyệt xem,
    /// không tải về và không đăng lại.
    /// </summary>
    public bool ImportThumbnails { get; set; }
    /// <summary>
    /// Đăng dạng bài NỀN MÀU của Facebook (text_format_preset_id) thay vì bài chữ thường.
    /// Đánh đổi đã cân nhắc: nền màu chỉ cho 130 KÝ TỰ và không đính được ảnh, đổi lại bài nổi
    /// bật hơn hẳn trên bảng tin. Link nguồn không nằm trong thân bài mà xuống bình luận đầu
    /// tiên (PostSourceLinkAsComment) — nhờ vậy giữ URL GỐC đầy đủ mà tiêu đề vẫn đủ chỗ.
    /// </summary>
    public bool UseFacebookBackground { get; set; } = true;
    /// <summary>Mã nền Facebook. Mặc định tím đậm — dễ đọc chữ trắng, hợp tông giáo dục.</summary>
    public string FacebookBackgroundPresetId { get; set; } = "106018623298955";
    /// <summary>Trần cứng của Facebook cho bài nền màu.</summary>
    public int BackgroundPostMaxChars { get; set; } = 130;
    /// <summary>
    /// Đăng link bài gốc xuống BÌNH LUẬN đầu tiên thay vì nhét vào thân bài.
    /// Nhờ vậy giữ được URL gốc đầy đủ mà không phải rút gọn, và tiêu đề vẫn đủ 130 ký tự.
    /// </summary>
    public bool PostSourceLinkAsComment { get; set; } = true;

    /// <summary>
    /// false = duyệt xong bài dừng ở Approved cho người rà trên /bulk/{batchId} rồi mới lên lịch.
    /// Nên giữ false vài tuần đầu để soi chất lượng nội dung AI trước khi thả tự động.
    /// </summary>
    public bool AutoScheduleOnApprove { get; set; }
    public List<string> ScheduleTimeSlots { get; set; } = ["08:00", "12:00", "20:00"];
    public string ScheduleTimezone { get; set; } = "Asia/Ho_Chi_Minh";
    public int ScheduleJitterMinutes { get; set; } = 12;

    /// <summary>Mặc định gán cho post sinh ra khi duyệt, nếu request không chỉ định.</summary>
    public Guid? CrawlPromptTemplateId { get; set; }
    public string DefaultObjective { get; set; } = "Chia sẻ tin giáo dục mới tới phụ huynh và học sinh";

    /// <summary>Cho AI đọc và chấm điểm tin trước khi đưa vào hàng chờ duyệt.</summary>
    /// <summary>
    /// Luồng HAI CỬA: duyệt tin = đưa lên website (CỬA 1), đăng fanpage là bước riêng (CỬA 2).
    ///
    /// Tắt thì ApproveAsync chạy y như cũ — fan-out post rồi đăng thẳng Facebook. Cờ này bắt
    /// buộc phải có vì ApproveAsync là đường mà CẢ web lẫn Telegram đều đi: một lỗi ở bước
    /// viết bài web mà không có đường lui sẽ chặn toàn bộ việc duyệt tin, kể cả lệnh /dang
    /// trên điện thoại.
    /// </summary>
    public bool TwoGateFlow { get; set; } = true;

    public bool ScreenEnabled { get; set; } = true;
    /// <summary>
    /// Dưới mức này thì đánh Filtered, không đưa cho người duyệt.
    /// 60 là mức đo được: câu đố và tin vụ việc rơi vào 10–35, tin chính sách 75–95,
    /// vùng 45–65 là tin giáo dục nhưng mỏng — để 60 nghĩa là thà bỏ sót hơn làm phiền.
    /// </summary>
    public int MinQualityScore { get; set; } = 60;
    /// <summary>Đo thật: gateway vietai chấm một bài mất ~28s, để 30s là hụt gần hết.</summary>
    public int ScreenTimeoutSeconds { get; set; } = 120;

    public int ArticleRetentionDays { get; set; } = 60;

    public DedupOptions Dedup { get; set; } = new();
}

/// <summary>
/// Ngưỡng chống trùng. Bậc thang rẻ → đắt: guid/url → hash tuyệt đối → SimHash+Jaccard → AI.
///
/// Các con số dưới đây ĐO TRÊN FEED THẬT (280 bài từ dantri/vnexpress/thanhnien/giaoduc.net.vn,
/// 39.060 cặp), không phải chọn theo cảm tính:
///   - Cặp trùng thật rơi vào d∈[12,21], j∈[0.53,0.86]
///   - Cặp KHÁC tin cũng rơi vào d∈[18,21], j∈[0.50,0.59]
///     (vd "Hà Tĩnh giảm 556 hiệu trưởng" ↔ "Tuyên Quang chọn hiệu trưởng" — khác tỉnh, khác tin)
/// Hai vùng CHỒNG LÊN NHAU, nên không có ngưỡng số học nào tách được. Đó là lý do bậc AI
/// tồn tại: nó xử đúng vùng chồng lấn mà điểm số bó tay.
/// </summary>
public class DedupOptions
{
    /// <summary>Chỉ so với tin/bài trong ngần này ngày. Chống trùng tin cũ hơn là vô nghĩa.</summary>
    public int LookbackDays { get; set; } = 14;
    /// <summary>
    /// Trần số vân tay nạp mỗi lượt (mới nhất trước). 7 feed × ~200 bài mới/ngày × 14 ngày
    /// ≈ 2.800 dòng — nạp một lần mỗi tick rồi so trong bộ nhớ là đủ nhanh và KHÔNG mất recall.
    /// </summary>
    public int MaxWindowRows { get; set; } = 5000;

    /// <summary>Chồng lấp token tiêu đề ≥ ngưỡng này thì chốt trùng luôn, khỏi gọi AI.</summary>
    public double DuplicateJaccardMin { get; set; } = 0.70;
    /// <summary>Khoảng cách Hamming ≤ ngưỡng này thì đẩy sang AI thẩm định.</summary>
    public int BorderlineHammingMax { get; set; } = 16;
    /// <summary>Hoặc chồng lấp tiêu đề ≥ ngưỡng này thì cũng đẩy sang AI.</summary>
    public double BorderlineJaccardMin { get; set; } = 0.40;

    public bool AiJudgeEnabled { get; set; } = true;
    public int AiJudgeMaxCandidates { get; set; } = 3;
    public int AiJudgeTimeoutSeconds { get; set; } = 20;
}
