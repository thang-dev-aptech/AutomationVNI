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
public static class FullMediaScoreReuse
{
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
