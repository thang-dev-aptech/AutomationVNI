namespace Backend.Modules.Campaign.FullMedia.Scoring;

/// <summary>
/// Kiểm phiếu của một page (bộ chấm bỏ phiếu): <paramref name="Yes"/> phiếu YES trong
/// <paramref name="Cast"/> phiếu đã gửi, trên tổng <paramref name="Total"/> model.
/// Cast &lt; Total nghĩa là dừng sớm: điểm yes/Total chỉ là cận DƯỚI.
/// </summary>
public sealed record PageFitVoteTally(int Yes, int Cast, int Total);

/// <param name="Votes">
/// Kiểm phiếu theo page. null (mặc định) = điểm luôn đầy đủ (bộ chấm Logprobs, giả lập trong test).
/// </param>
public sealed record PageFitResult(
    IReadOnlyDictionary<Guid, double> Scores,
    decimal EstimatedCostUsd,
    int RequestCount,
    IReadOnlyDictionary<Guid, PageFitVoteTally>? Votes = null);
