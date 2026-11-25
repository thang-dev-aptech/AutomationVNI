namespace Backend.Modules.Campaign.FullMedia;

/// <summary>
/// Khi nào điểm đã lưu còn dùng lại được lúc chấm lại (cùng ProfileHash).
/// <para>
/// Với dừng sớm, <see cref="CampaignMediaScoreModel.Score"/> là CẬN DƯỚI (yes/N, phiếu bỏ tính NO).
/// Nếu MinFitScore bị hạ, cận trên (yes + còn_lại)/N có thể đã đạt ngưỡng mới → phải chấm lại.
/// Mọi so sánh ngưỡng chỉ qua <see cref="FitThreshold.Passes"/>. Fail closed: dữ liệu bất thường
/// → không tin → chấm lại.
/// </para>
/// </summary>
/// <summary>Phân loại một page ứng viên theo điểm đã lưu ở ngưỡng hiện tại.</summary>
public enum FullMediaScoreClass
{
    /// <summary>(V) Tin được và đạt cả hai lượt — đăng.</summary>
    Valid,

    /// <summary>(R) Tin được nhưng ít nhất một lượt không đạt — loại dứt khoát.</summary>
    RejectedDefinitively,

    /// <summary>(U) Chưa quyết định được — phải chấm lại trước khi quyết định.</summary>
    Unknown,
}

public static class FullMediaScoreReuse
{
    /// <summary>
    /// Hàm phân loại duy nhất (V)/(R)/(U) cho một page. Chỉ dùng <see cref="IsReusable"/> và
    /// <see cref="FitThreshold.Passes"/> cho mọi so sánh ngưỡng. Thiếu điểm, ProfileHash đổi/NULL
    /// hoặc điểm chưa tin được (dừng sớm mà cận trên còn có thể đạt) → (U).
    /// Lượt 1 tin được và không đạt → (R) ngay (không cần lượt 2 để loại).
    /// </summary>
    public static FullMediaScoreClass Classify(
        CampaignMediaScoreModel? pass1,
        CampaignMediaScoreModel? pass2,
        string? currentProfileHash,
        double currentMinFitScore)
    {
        if (pass1 is null)
            return FullMediaScoreClass.Unknown;
        if (string.IsNullOrEmpty(currentProfileHash))
            return FullMediaScoreClass.Unknown;
        if (string.IsNullOrEmpty(pass1.ProfileHash)
            || !string.Equals(pass1.ProfileHash, currentProfileHash, StringComparison.Ordinal))
            return FullMediaScoreClass.Unknown;
        if (!IsReusable(pass1, currentMinFitScore))
            return FullMediaScoreClass.Unknown;
        if (!FitThreshold.Passes(pass1.Score, currentMinFitScore))
            return FullMediaScoreClass.RejectedDefinitively;

        if (pass2 is null)
            return FullMediaScoreClass.Unknown;
        if (string.IsNullOrEmpty(pass2.ProfileHash)
            || !string.Equals(pass2.ProfileHash, currentProfileHash, StringComparison.Ordinal))
            return FullMediaScoreClass.Unknown;
        if (!IsReusable(pass2, currentMinFitScore))
            return FullMediaScoreClass.Unknown;
        if (!FitThreshold.Passes(pass2.Score, currentMinFitScore))
            return FullMediaScoreClass.RejectedDefinitively;

        return FullMediaScoreClass.Valid;
    }

    public static bool IsReusable(CampaignMediaScoreModel row, double currentMinFitScore)
    {
        ArgumentNullException.ThrowIfNull(row);

        // Cả ba NULL: điểm đầy đủ / dữ liệu cũ (chưa có dừng sớm) → tin.
        if (row.VotesYes is null && row.VotesCast is null && row.VotesTotal is null)
            return true;

        // Thiếu một phần → bất thường.
        if (row.VotesYes is not { } yes || row.VotesCast is not { } cast || row.VotesTotal is not { } total)
            return false;

        if (total <= 0 || yes < 0 || cast < 0 || cast > total || yes > cast)
            return false;

        // Đã gửi đủ phiếu → điểm đầy đủ, đúng ở mọi ngưỡng.
        if (cast == total)
            return true;

        // Dừng sớm: chỉ tin khi cận trên vẫn KHÔNG đạt ngưỡng hiện tại.
        var upper = (double)(yes + (total - cast)) / total;
        return !FitThreshold.Passes(upper, currentMinFitScore);
    }
}
