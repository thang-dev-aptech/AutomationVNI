namespace Backend.Modules.Campaign.Enums;

/// <summary>Loại bài nguồn chiến dịch lọc theo media.</summary>
public enum CampaignMediaType
{
    Image = 1,
    Video = 2,
}

/// <summary>Chiến lược hình ảnh — chỉ khi MediaType = Image (giống RecycleImageStrategy).</summary>
public enum CampaignImageStrategy
{
    KeepOld = 1,
    VectorSearch = 2,
}

/// <summary>Chế độ lịch: theo thứ đã chọn hoặc cả tuần (lặp hàng tuần).</summary>
public enum CampaignScheduleMode
{
    ByWeekday = 1,
    AllWeek = 2,
}

public enum CampaignStatus
{
    Running = 1,
    Paused = 2,
    Ended = 3,
}
