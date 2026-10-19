namespace Backend.Modules.Campaign.FullMedia.Scoring;

/// <summary>
/// Tuỳ chọn lần gọi chấm điểm. Exhaustive=true (Vote): luôn gửi đủ N phiếu mỗi page
/// (dùng cho kiểm chuẩn cần xếp hạng). Mặc định false: tôn trọng PageFit:EarlyExit.
/// </summary>
public sealed record PageFitScoreOptions
{
    public bool Exhaustive { get; init; }
}
