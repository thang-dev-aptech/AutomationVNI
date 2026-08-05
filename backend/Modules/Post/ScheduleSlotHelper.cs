namespace Backend.Modules.Post;

/// <summary>
/// Rải lịch đăng theo khung giờ vàng. Tách khỏi PostController để luồng duyệt tin crawl
/// dùng lại đúng một cách tính — hai nơi tính lịch khác nhau thì bài crawl và bài bulk
/// sẽ rơi vào khung giờ lệch nhau mà không ai biết vì sao.
/// </summary>
public static class ScheduleSlotHelper
{
    public static readonly string[] DefaultSlots = ["08:00", "12:00", "20:00"];
    public const string DefaultTimezone = "Asia/Ho_Chi_Minh";

    /// <summary>
    /// Sinh danh sách mốc UTC theo khung giờ (local) rải qua các ngày, chỉ lấy mốc tương lai.
    /// <paramref name="jitterMinutes"/> &gt; 0 thì mỗi mốc lệch ngẫu nhiên trong ±jitter phút để
    /// không đăng khít cùng một phút mỗi ngày. Mốc lệch không bao giờ bị kéo về quá khứ và luôn
    /// giữ thứ tự tăng dần (mốc sau ít nhất hơn mốc trước 1 phút).
    /// </summary>
    public static List<DateTime> ComputeSlotTimesUtc(
        DateTime startUtc, IReadOnlyList<string>? slots, string? timezone, int count, int jitterMinutes = 0)
    {
        if (count <= 0) return [];
        var tz = PendingScheduleHelper.ResolveTimeZone(
            string.IsNullOrWhiteSpace(timezone) ? DefaultTimezone : timezone);

        var slotSpans = (slots ?? [])
            .Select(s => TimeSpan.TryParse(s?.Trim(), out var ts) ? ts : (TimeSpan?)null)
            .Where(ts => ts.HasValue).Select(ts => ts!.Value)
            .OrderBy(ts => ts).ToList();
        if (slotSpans.Count == 0)
            slotSpans = [new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), new TimeSpan(20, 0, 0)];

        var nowUtc = DateTime.UtcNow.AddMinutes(1);
        var effectiveStartUtc = startUtc > nowUtc ? startUtc : nowUtc;
        var day = TimeZoneInfo.ConvertTimeFromUtc(effectiveStartUtc, tz).Date;

        var jitter = Math.Clamp(jitterMinutes, 0, 240);
        var rnd = new Random();

        var result = new List<DateTime>();
        var guard = 0;
        while (result.Count < count && guard++ < 500)
        {
            foreach (var slot in slotSpans)
            {
                var local = DateTime.SpecifyKind(day + slot, DateTimeKind.Unspecified);
                var baseUtc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
                // So với mốc bắt đầu (đã gồm cả giờ), không chỉ so với hiện tại — nhập
                // "09:45 ngày 25/07" thì khung 09:00 của chính ngày 25/07 phải bị bỏ qua.
                if (baseUtc <= effectiveStartUtc) continue;

                var utc = baseUtc;
                if (jitter > 0)
                {
                    // Lệch theo giây để hai bài cùng khung giờ không trùng phút.
                    utc = baseUtc.AddSeconds(rnd.Next(-jitter * 60, jitter * 60 + 1));
                    if (utc <= effectiveStartUtc) utc = baseUtc; // lệch âm không được vượt qua mốc bắt đầu
                }

                // Giữ thứ tự tăng dần: jitter lớn hơn khoảng cách 2 khung giờ có thể làm đảo mốc.
                if (result.Count > 0 && utc <= result[^1])
                    utc = result[^1].AddMinutes(1);

                result.Add(utc);
                if (result.Count >= count) break;
            }
            day = day.AddDays(1);
        }
        return result;
    }
}
