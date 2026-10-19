namespace Backend.Modules.Campaign.FullMedia;

/// <summary>
/// Điểm so sánh ngưỡng duy nhất — không làm tròn (0.9399 không thành 0.94).
/// Với bộ chấm bỏ phiếu (Mode=Vote), điểm là tỉ lệ đồng thuận YES/N, không phải xác suất;
/// với N≤16 model, ngưỡng 0,94 thực chất yêu cầu nhất trí tuyệt đối (ví dụ 2/3 không đạt).
/// </summary>
public static class FitThreshold
{
    public const double DefaultMinFitScore = 0.94;

    public static bool Passes(double score, double minFitScore)
        => score >= minFitScore;
}
