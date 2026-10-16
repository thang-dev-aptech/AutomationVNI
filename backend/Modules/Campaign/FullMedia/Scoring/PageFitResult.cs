namespace Backend.Modules.Campaign.FullMedia.Scoring;

public sealed record PageFitResult(
    IReadOnlyDictionary<Guid, double> Scores,
    decimal EstimatedCostUsd,
    int RequestCount);
