namespace Backend.Modules.Campaign.FullMedia.Scoring;

public interface IPageFitScorer
{
    Task<PageFitResult> ScoreAsync(
        string imageStateText,
        IReadOnlyList<PageFitCandidate> pages,
        CancellationToken ct = default);
}
