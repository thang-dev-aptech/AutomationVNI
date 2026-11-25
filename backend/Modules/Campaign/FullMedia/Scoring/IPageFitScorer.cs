namespace Backend.Modules.Campaign.FullMedia.Scoring;

public interface IPageFitScorer
{
    Task<PageFitResult> ScoreAsync(
        string imageStateText,
        IReadOnlyList<PageFitCandidate> pages,
        CancellationToken ct = default);

    /// <summary>
    /// Mặc định gọi lại phương thức 3 tham số. VotingPageFitScorer ghi đè để tôn trọng
    /// <see cref="PageFitScoreOptions.Exhaustive"/>; bộ chấm khác bỏ qua Exhaustive.
    /// </summary>
    Task<PageFitResult> ScoreAsync(
        string imageStateText,
        IReadOnlyList<PageFitCandidate> pages,
        PageFitScoreOptions options,
        CancellationToken ct = default)
        => ScoreAsync(imageStateText, pages, ct);
}
