namespace Backend.Modules.Campaign.FullMedia;

/// <summary>Cấu hình Full Media Pipeline (t2: Vision). Enabled mặc định false.</summary>
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
}
