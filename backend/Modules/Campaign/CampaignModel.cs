using Backend.Modules.Campaign.Enums;
using Backend.Shared;

namespace Backend.Modules.Campaign;

/// <summary>
/// Chiến dịch lặp hàng tuần (CAMPAIGN-01). Không FK constraint.
/// WeekdaysJson / PublishTimesJson lưu JSON (TEXT).
/// </summary>
public class CampaignModel : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public CampaignMediaType MediaType { get; set; } = CampaignMediaType.Image;
    /// <summary>Chỉ dùng khi MediaType = Image; null khi Video.</summary>
    public CampaignImageStrategy? ImageStrategy { get; set; }
    public CampaignScheduleMode ScheduleMode { get; set; } = CampaignScheduleMode.AllWeek;
    /// <summary>JSON mảng int 1=Thứ 2 … 7=Chủ nhật (giờ VN). Rỗng khi AllWeek.</summary>
    public string WeekdaysJson { get; set; } = "[]";
    /// <summary>JSON mảng chuỗi "HH:mm" (24h).</summary>
    public string PublishTimesJson { get; set; } = "[]";
    public int JitterMinutes { get; set; }
    /// <summary>Ngày bắt đầu (phần ngày; lưu UTC midnight của ngày lịch).</summary>
    public DateTime StartDate { get; set; }
    /// <summary>Ngày kết thúc tuỳ chọn (≥ StartDate).</summary>
    public DateTime? EndDate { get; set; }
    public CampaignStatus Status { get; set; } = CampaignStatus.Running;
}
