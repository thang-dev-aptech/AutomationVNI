namespace Backend.Modules.Campaign.FullMedia;

/// <summary>
/// Cấu hình Full Media Pipeline (Vision + worker). Enabled mặc định false.
/// Một mục cấu hình duy nhất — không tách FullMediaOptions trùng nghĩa.
/// </summary>
public class FullMediaPipelineOptions
{
    public const string SectionName = "FullMediaPipeline";

    public bool Enabled { get; set; }

    public string? VisionModelA { get; set; }
    public string? VisionModelB { get; set; }
    public string? VisionFallbackModel { get; set; }

    /// <summary>Giới hạn dung lượng ảnh đọc vào bộ nhớ (bytes).</summary>
    public long MaxImageBytes { get; set; } = 5_242_880; // 5 MiB

    /// <summary>Confidence dưới mức này → thử model dự phòng.</summary>
    public double MinVisionConfidence { get; set; } = 0.5;

    /// <summary>Số lần thử model dự phòng tối đa sau mô tả chính (mặc định 1).</summary>
    public int MaxVisionFallbackAttempts { get; set; } = 1;

    /// <summary>Chu kỳ worker (giây). Thực tế tối thiểu 15.</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Ngưỡng nhận ảnh (≥). Mặc định 0.94 — so sánh qua <see cref="FitThreshold"/>.</summary>
    public double MinFitScore { get; set; } = FitThreshold.DefaultMinFitScore;

    public int MaxImagesPerTick { get; set; } = 5;
    public int MaxImagesPerDay { get; set; } = 100;
    public decimal MaxDailyCostUsd { get; set; } = 10m;

    /// <summary>Trần số lần chấm lại ảnh Rejected khi InputHash đổi.</summary>
    public int MaxRescores { get; set; } = 3;

    /// <summary>
    /// Trần số lần Pending do lỗi (không gồm chờ khe/hồ sơ/tạm dừng).
    /// Hết lần → Failed.
    /// </summary>
    public int MaxPendingAttempts { get; set; } = 5;

    /// <summary>Timeout mỗi cuộc gọi ngoài (Vision / Score / Caption), giây.</summary>
    public int StageTimeoutSeconds { get; set; } = 90;

    /// <summary>Số lỗi liên tiếp trước khi ngắt mạch.</summary>
    public int CircuitBreakerFailures { get; set; } = 5;

    /// <summary>Thời gian nghỉ khi mạch mở (giây).</summary>
    public int CircuitBreakerCooldownSeconds { get; set; } = 120;

    /// <summary>Trễ khởi động worker (giây), giống CampaignWorker.</summary>
    public int StartupDelaySeconds { get; set; } = 25;
}
