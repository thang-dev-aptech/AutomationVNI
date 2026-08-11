using Microsoft.Extensions.Options;

namespace Backend.Modules.PageMetrics;

public class PageMetricsOptions
{
    /// <summary>Tắt thì dashboard hiện số của lần đồng bộ cuối, không tự làm mới.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Số giờ giữa hai lượt. 6 tiếng = 4 lượt/ngày, đủ để số trong ngày không quá cũ.</summary>
    public int IntervalHours { get; set; } = 6;

    /// <summary>Số bài gần nhất quét mỗi page mỗi lượt.</summary>
    /// <remarks>
    /// 100 là cố ý. Đo thật: page nhiều nhất trong 18 page có 57 bài đã đồng bộ, và bài cũ hơn
    /// một tháng gần như không còn phát sinh tương tác. Quét sâu hơn chỉ tốn lượt gọi API để
    /// xác nhận những con số không đổi.
    /// </remarks>
    public int MaxPostsPerChannel { get; set; } = 100;
}

/// <summary>
/// Chạy đồng bộ chỉ số theo chu kỳ.
///
/// Lượt ĐẦU TIÊN chạy ngay sau khi khởi động chứ không đợi hết chu kỳ — máy mới dựng lên mà
/// dashboard trống trơn 6 tiếng thì người ta tưởng tính năng hỏng.
/// </summary>
public class PageMetricsWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PageMetricsOptions> options,
    ILogger<PageMetricsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.Enabled)
        {
            logger.LogInformation("Đồng bộ chỉ số page: đang TẮT (PageMetrics:Enabled = false)");
            return;
        }

        // Chờ migration và seed xong. Chạy sớm hơn thì truy vấn đầu tiên đâm vào bảng chưa tồn tại.
        try { await Task.Delay(TimeSpan.FromSeconds(40), ct); }
        catch (OperationCanceledException) { return; }

        var interval = TimeSpan.FromHours(Math.Max(1, opt.IntervalHours));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<PageMetricsSyncService>();
                var result = await svc.SyncAllAsync(opt.MaxPostsPerChannel, ct);

                if (result.Failed > 0)
                {
                    // Mức Warning chứ không phải Information: page hỏng token sẽ đứng im mãi mãi
                    // và dashboard cứ hiện số của tuần trước như thể vẫn đang cập nhật.
                    var names = string.Join(", ",
                        result.Details.Where(x => !x.Ok).Select(x => $"{x.PageName} ({x.Error})"));
                    logger.LogWarning(
                        "Đồng bộ chỉ số: {Failed}/{Total} page HỎNG — {Names}",
                        result.Failed, result.Channels, names);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lượt đồng bộ chỉ số hỏng toàn bộ");
            }

            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
