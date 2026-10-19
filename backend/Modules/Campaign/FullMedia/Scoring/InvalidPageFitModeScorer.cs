namespace Backend.Modules.Campaign.FullMedia.Scoring;

/// <summary>Đăng ký khi PageFit:Mode không phải Vote|Logprobs — fail closed khi gọi.</summary>
internal sealed class InvalidPageFitModeScorer(string mode) : IPageFitScorer
{
    public Task<PageFitResult> ScoreAsync(
        string imageStateText,
        IReadOnlyList<PageFitCandidate> pages,
        CancellationToken ct = default)
    {
        throw new PageFitPermanentException(
            "invalid_mode",
            $"PageFit:Mode '{mode}' is not supported; use Vote or Logprobs");
    }
}
