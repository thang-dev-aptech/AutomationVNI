using Backend.Modules.ContentCrawl.Enums;
using Backend.Modules.Post.Enums;
using Backend.Shared;

namespace Backend.Modules.ContentCrawl;

// ── Nguồn cào ───────────────────────────────────────────────────────────────

public class CreateCrawlSourceRequest
{
    public string Name { get; set; } = string.Empty;
    public CrawlSourceType SourceType { get; set; } = CrawlSourceType.WebPage;
    public string Url { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Giờ cào cố định "HH:mm" giờ VN, vd ["08:00","14:00"]. Có giá trị thì bỏ qua IntervalMinutes.</summary>
    public List<string>? CrawlTimes { get; set; }
    public int IntervalMinutes { get; set; } = 30;
    public int MaxItemsPerRun { get; set; } = 30;
    public int LookbackHours { get; set; } = 48;
    public List<string>? IncludeKeywords { get; set; }
    public List<string>? ExcludeKeywords { get; set; }
    public List<Guid>? DefaultChannelIds { get; set; }
    public string? BrowserProfile { get; set; }
}

public class UpdateCrawlSourceRequest
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public Guid? CategoryId { get; set; }
    public bool? IsActive { get; set; }
    public List<string>? CrawlTimes { get; set; }
    public int? IntervalMinutes { get; set; }
    public int? MaxItemsPerRun { get; set; }
    public int? LookbackHours { get; set; }
    public List<string>? IncludeKeywords { get; set; }
    public List<string>? ExcludeKeywords { get; set; }
    public List<Guid>? DefaultChannelIds { get; set; }
    public string? BrowserProfile { get; set; }
}

public class CrawlSourceResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CrawlSourceType SourceType { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? SiteDomain { get; set; }
    public Guid? CategoryId { get; set; }
    public bool IsActive { get; set; }
    public List<string> CrawlTimes { get; set; } = [];
    public int IntervalMinutes { get; set; }
    public int MaxItemsPerRun { get; set; }
    public int LookbackHours { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }
    public List<string> IncludeKeywords { get; set; } = [];
    public List<string> ExcludeKeywords { get; set; } = [];
    public List<Guid> DefaultChannelIds { get; set; } = [];
    public string? BrowserProfile { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ── Lượt cào ────────────────────────────────────────────────────────────────

public class CrawlRunResponse
{
    public Guid Id { get; set; }
    public Guid CrawlSourceId { get; set; }
    public string? SourceName { get; set; }
    public CrawlRunStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int ItemsFetched { get; set; }
    public int ItemsNew { get; set; }
    public int ItemsDuplicate { get; set; }
    public int ItemsFiltered { get; set; }
    public int DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? TriggerSource { get; set; }
}

// ── Tin đã cào ──────────────────────────────────────────────────────────────

public class CrawledArticleFilterRequest : PagedFilterRequest
{
    public Guid? CrawlSourceId { get; set; }
    public CrawledArticleStatus? Status { get; set; }
    public List<CrawledArticleStatus>? Statuses { get; set; }
    public string? Keyword { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class CrawledArticleResponse
{
    public Guid Id { get; set; }
    public Guid CrawlSourceId { get; set; }
    public string? SourceName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? SourceCategory { get; set; }
    public string? ThumbnailUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime FetchedAt { get; set; }

    public CrawledArticleStatus Status { get; set; }
    public DedupMethod DuplicateMethod { get; set; }
    public DuplicateTarget DuplicateTarget { get; set; }
    public Guid? DuplicateOfId { get; set; }
    public double? DuplicateScore { get; set; }
    public string? DuplicateReason { get; set; }


    public string? CategorySlug { get; set; }
    public int? ShortCode { get; set; }
    public int? QualityScore { get; set; }
    public string? ScreenTopic { get; set; }
    public string? ScreenReason { get; set; }
    public string? ScreenSummary { get; set; }
    public string? RejectReason { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? ResultBatchId { get; set; }
    public int ResultPostCount { get; set; }
}

public class ApproveCrawledArticleRequest
{
    /// <summary>Bỏ trống thì lấy DefaultChannelIds của nguồn.</summary>
    public List<Guid>? ChannelIds { get; set; }
    /// <summary>
    /// Mặc định TextOnly: bài tin chỉ cần tóm tắt + link nguồn, không sinh ảnh.
    /// Muốn có banner AI thì đổi sang FullAI cho riêng lần duyệt đó.
    /// </summary>
    public GenerationFlow GenerationFlow { get; set; } = GenerationFlow.TextOnly;
    public Guid? PromptTemplateId { get; set; }
    public string? Objective { get; set; }
    /// <summary>Mốc bắt đầu rải lịch. Bỏ trống = từ bây giờ.</summary>
    public DateTime? StartAtUtc { get; set; }
    /// <summary>Ghi đè AutoScheduleOnApprove trong config cho riêng lần duyệt này.</summary>
    public bool? AutoSchedule { get; set; }

    /// <summary>
    /// Đăng thẳng lên Facebook ngay khi sinh nội dung xong, không qua bước lên lịch.
    /// Dành cho lệnh /dang trên Telegram: sếp gõ một câu là bài lên, rồi bot báo link về.
    /// </summary>
    public bool AutoPublish { get; set; }
}

public class ApproveCrawledArticleResult
{
    public Guid ArticleId { get; set; }
    public Guid BatchId { get; set; }
    public int Created { get; set; }
    public List<Guid> PostIds { get; set; } = [];
    /// <summary>Tên page thực sự nhận bài — để Telegram nói rõ đăng lên đâu, không bắt đoán.</summary>
    public List<string> Channels { get; set; } = [];
    public List<DateTime> ScheduledTimesUtc { get; set; } = [];
    public string Message { get; set; } = string.Empty;
}

public class RejectCrawledArticleRequest
{
    public string? Reason { get; set; }
}

public class CrawlNowRequest
{
    public string? TriggerSource { get; set; }
}

/// <summary>Kết quả thử một feed — parse thật nhưng KHÔNG ghi gì vào DB.</summary>
public class TestCrawlSourceResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public int ItemCount { get; set; }
    public List<TestCrawlItem> Items { get; set; } = [];
}

public class TestCrawlItem
{
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    /// <summary>Số ký tự toàn văn bóc được — 0 nghĩa là bóc hỏng, cần xem lại.</summary>
    public int ContentLength { get; set; }
    public string? ContentPreview { get; set; }
    public string Link { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? ThumbnailUrl { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public class CrawlInboxSummaryResponse
{
    public Dictionary<string, int> ByStatus { get; set; } = [];
    public int PendingCount { get; set; }
    public int TotalActiveSources { get; set; }
    public DateTime? LastRunAt { get; set; }
}
