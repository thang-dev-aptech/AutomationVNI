using Backend.Modules.Campaign.Enums;
using Backend.Modules.Post.Enums;
using Backend.Shared;

namespace Backend.Modules.Campaign;

public class CreateCampaignRequest
{
    public string Name { get; set; } = string.Empty;
    public CampaignMediaType MediaType { get; set; } = CampaignMediaType.Image;
    public CampaignImageStrategy? ImageStrategy { get; set; }
    public List<Guid>? ChannelIds { get; set; }
    public List<Guid>? ChannelGroupIds { get; set; }
    public CampaignScheduleMode ScheduleMode { get; set; } = CampaignScheduleMode.AllWeek;
    /// <summary>1=Thứ 2 … 7=Chủ nhật. Bắt buộc ≥1 khi ScheduleMode = ByWeekday.</summary>
    public List<int>? Weekdays { get; set; }
    /// <summary>Danh sách "HH:mm", không trùng.</summary>
    public List<string> PublishTimes { get; set; } = [];
    public int JitterMinutes { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class UpdateCampaignRequest
{
    public string? Name { get; set; }
    public CampaignMediaType? MediaType { get; set; }
    public CampaignImageStrategy? ImageStrategy { get; set; }
    /// <summary>Null = không đổi; list = thay thế toàn bộ.</summary>
    public List<Guid>? ChannelIds { get; set; }
    /// <summary>Null = không đổi; list = thay thế toàn bộ.</summary>
    public List<Guid>? ChannelGroupIds { get; set; }
    public CampaignScheduleMode? ScheduleMode { get; set; }
    public List<int>? Weekdays { get; set; }
    public List<string>? PublishTimes { get; set; }
    public int? JitterMinutes { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public class CampaignFilterRequest : PagedFilterRequest
{
    public CampaignStatus? Status { get; set; }
}

public class CampaignResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CampaignMediaType MediaType { get; set; }
    public CampaignImageStrategy? ImageStrategy { get; set; }
    public List<Guid> ChannelIds { get; set; } = [];
    public List<Guid> ChannelGroupIds { get; set; } = [];
    public CampaignScheduleMode ScheduleMode { get; set; }
    public List<int> Weekdays { get; set; } = [];
    public List<string> PublishTimes { get; set; } = [];
    public int JitterMinutes { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public CampaignStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Dòng danh sách chiến dịch + số liệu gom nhóm (không N+1).</summary>
public class CampaignSummaryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CampaignMediaType MediaType { get; set; }
    public CampaignScheduleMode ScheduleMode { get; set; }
    public CampaignStatus Status { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Số page thực chạy (kênh lẻ ∪ thành viên nhóm hiện tại).</summary>
    public int PageCount { get; set; }
    /// <summary>Bài sắp tới: Queued / Approved / Scheduled.</summary>
    public int UpcomingCount { get; set; }
    public int PublishedCount { get; set; }
    public int FailedCount { get; set; }
}

public class CampaignDetailResponse
{
    public CampaignResponse Campaign { get; set; } = new();
    public List<CampaignPageStatsResponse> Pages { get; set; } = [];
}

public class CampaignPageStatsResponse
{
    public Guid SocialChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public int ScheduledCount { get; set; }
    public int PublishedCount { get; set; }
    public int FailedCount { get; set; }
    public DateTime? NextScheduledAt { get; set; }
    public string? Warning { get; set; }
}

public class CampaignPagePostsRequest : PagedFilterRequest
{
}

public class CampaignPagePostItemResponse
{
    public Guid Id { get; set; }
    public DateTime? ScheduledPublishAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public PostStatus Status { get; set; }
    public string? ContentSnippet { get; set; }
    public string? Title { get; set; }
    public int MediaCount { get; set; }
    public string? ThumbnailUrl { get; set; }
    public Guid? PrimaryMediaId { get; set; }
}
