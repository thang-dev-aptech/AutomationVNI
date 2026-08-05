using Backend.Modules.ContentCrawl.Enums;
using Backend.Shared;

namespace Backend.Modules.ContentCrawl;

/// <summary>Một nguồn cào: feed RSS của báo, hoặc fanpage Facebook (Phase 3).</summary>
public class CrawlSourceModel : BaseEntity
{
    public string Name { get; set; } = string.Empty;              // "Dân trí — Giáo dục"
    public CrawlSourceType SourceType { get; set; } = CrawlSourceType.WebPage;
    public string Url { get; set; } = string.Empty;
    public string? SiteDomain { get; set; }                       // "dantri.com.vn" — hiển thị + giãn nhịp theo site
    public Guid? CategoryId { get; set; }                         // No FK — gợi ý danh mục cho post sinh ra
    public bool IsActive { get; set; } = true;

    public int IntervalMinutes { get; set; } = 30;
    public int MaxItemsPerRun { get; set; } = 30;                 // chặn feed trả 1000 item (vietnamnet)
    public int LookbackHours { get; set; } = 48;

    public DateTime? LastRunAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }                  // càng lỗi càng giãn chu kỳ

    public string? IncludeKeywords { get; set; }                  // JSON array; rỗng = nhận hết
    public string? ExcludeKeywords { get; set; }                  // JSON array — chặn tin pháp luật/tiêu cực
    /// <summary>Kênh mặc định khi duyệt nhanh qua Telegram (JSON array Guid). No FK.</summary>
    public string? DefaultChannelIds { get; set; }
    /// <summary>Phase 3 — tên profile trình duyệt OpenClaw giữ phiên đăng nhập.</summary>
    public string? BrowserProfile { get; set; }
}

/// <summary>Nhật ký một lượt cào. Dùng để báo Telegram và để soi vì sao một nguồn im lặng.</summary>
public class CrawlRunModel : BaseEntity
{
    public Guid CrawlSourceId { get; set; }                       // No FK
    public CrawlRunStatus Status { get; set; } = CrawlRunStatus.Running;
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public int ItemsFetched { get; set; }
    public int ItemsNew { get; set; }
    public int ItemsDuplicate { get; set; }
    public int ItemsFiltered { get; set; }
    public int DurationMs { get; set; }

    public string? ErrorMessage { get; set; }
    public string? TriggerSource { get; set; }                    // "worker" | "manual" | "telegram"
}
