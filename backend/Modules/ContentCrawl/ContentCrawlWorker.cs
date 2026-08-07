using Microsoft.Extensions.Options;

namespace Backend.Modules.ContentCrawl;

/// <summary>
/// Worker cào tin. Theo đúng khuôn PostGenerationWorker: guard Enabled + thoát sớm,
/// try/catch loại OperationCanceledException BÊN TRONG vòng lặp để một lần lỗi không giết
/// cả worker, và chặn sàn cho khoảng nghỉ.
///
/// Mỗi tick làm 3 việc, cố ý gộp vào một worker thay vì đẻ thêm hai BackgroundService nữa
/// (đã có sẵn 5 cái rồi).
/// </summary>
public class ContentCrawlWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ContentCrawlOptions> options,
    ILogger<ContentCrawlWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("ContentCrawlWorker bị tắt trong cấu hình");
            return;
        }

        logger.LogInformation(
            "ContentCrawlWorker chạy (chu kỳ={Interval}s, tối đa {Sources} nguồn/lượt, song song={Concurrency})",
            settings.IntervalSeconds, settings.MaxSourcesPerTick, settings.MaxConcurrency);

        // Chờ một nhịp cho migration và seed xong trước khi đụng DB.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FetchDueSourcesAsync(settings, stoppingToken);
                await ProcessArticlesAsync(stoppingToken);
                await ComposeQueuedArticlesAsync(stoppingToken);
                await SyncPublishedFingerprintsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ContentCrawlWorker lỗi vòng lặp");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, settings.IntervalSeconds)), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Cào TUẦN TỰ từng nguồn, MỘT scope cho mỗi nguồn (DbContext không thread-safe).
    ///
    /// KHÔNG được chạy song song: OpenClaw chỉ có MỘT tab trình duyệt dùng chung, hai nguồn
    /// cùng gọi /navigate sẽ đè lên nhau — cái này huỷ điều hướng của cái kia.
    /// Đã gặp thật: lịch 14:00 nổ, cả hai nguồn cùng chạy, một cái ăn net::ERR_ABORTED còn
    /// cái kia chạy "thành công" nhưng lấy về 0 bài vì đang đứng ở trang trắng.
    /// Ở quy mô vài nguồn × vài bài mỗi ngày thì tuần tự không hề chậm.
    /// </summary>
    private async Task FetchDueSourcesAsync(ContentCrawlOptions settings, CancellationToken ct)
    {
        List<Guid> dueIds;
        using (var scope = scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ContentCrawlRepository>();
            var sources = await repository.GetDueSourcesAsync(settings.MaxSourcesPerTick, ct);
            dueIds = sources.Select(s => s.Id).ToList();
        }
        if (dueIds.Count == 0) return;

        foreach (var id in dueIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var scope = scopeFactory.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<ContentCrawlPipelineService>();
                await pipeline.RunSourceAsync(id, "worker", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Một nguồn hỏng không được làm chết các nguồn còn lại.
                logger.LogError(ex, "Cào nguồn {SourceId} thất bại", id);
            }
        }
    }

    /// <summary>
    /// Chấm trùng + xào nháp chạy TUẦN TỰ trên một scope. SQLite ở journal mode mặc định
    /// (không WAL) serialize writer, ghi dồn từ nhiều scope là đường dẫn thẳng tới SQLITE_BUSY.
    /// </summary>
    private async Task ProcessArticlesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<ContentCrawlPipelineService>();
        var processed = await pipeline.ProcessPendingAsync(ct);
        if (processed > 0) logger.LogInformation("Đã xử lý {Count} tin (chấm trùng + xào nháp)", processed);
    }

    /// <summary>
    /// Viết những bài đang xếp hàng ở CỬA 1.
    ///
    /// TUẦN TỰ, MỘT scope mỗi bài. Không chạy song song vì mỗi bài là một lượt gọi AI 38 giây;
    /// bắn năm lượt cùng lúc là ăn giới hạn tốc độ của nhà cung cấp, và cả năm cùng hỏng thay
    /// vì hỏng một.
    ///
    /// Mỗi nhịp làm tối đa 3 bài để một hàng đợi dài không chặn hai việc kia của worker.
    /// </summary>
    private async Task ComposeQueuedArticlesAsync(CancellationToken ct)
    {
        List<Guid> queued;
        using (var scope = scopeFactory.CreateScope())
        {
            var publisher = scope.ServiceProvider
                .GetRequiredService<Backend.Modules.NewsSite.NewsPublisher>();
            queued = (await publisher.GetQueuedAsync(3, ct)).Select(x => x.Id).ToList();
        }
        if (queued.Count == 0) return;

        logger.LogInformation("Có {N} bài đang chờ viết", queued.Count);

        foreach (var newsId in queued)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var pipeline = scope.ServiceProvider.GetRequiredService<ContentCrawlPipelineService>();
                await pipeline.ComposeQueuedAsync(newsId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Viết bài {Id} lỗi", newsId);
            }
        }
    }

    private async Task SyncPublishedFingerprintsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<ContentCrawlPipelineService>();
        await pipeline.SyncPublishedPostFingerprintsAsync(200, ct);
    }
}
