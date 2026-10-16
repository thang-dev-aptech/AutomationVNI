namespace Backend.Modules.Campaign.FullMedia;

/// <summary>
/// Điểm so sánh ngưỡng duy nhất — không làm tròn (0.9399 không thành 0.94).
/// </summary>
public static class FitThreshold
{
    public const double DefaultMinFitScore = 0.94;

    public static bool Passes(double score, double minFitScore)
        => score >= minFitScore;
}
