using System.Text.Json;
using Backend.Modules.ContentCrawl;
using Microsoft.Extensions.Options;

namespace Backend.Modules.NewsSite;

/// <summary>
/// CỬA 1 của luồng duyệt: tin đã cào → AI viết bài → xuất bản lên website → dựng lại trang.
///
/// Tách khỏi controller vì cả ba nơi cùng gọi: giao diện web, lệnh Telegram, và
/// ContentCrawlPipelineService.ApproveAsync. Để logic trong controller thì hai nơi kia phải
/// gọi HTTP vòng lại chính mình.
///
/// KHÔNG BAO GIỜ ném. Trả null khi không xuất bản được, kèm lý do đã ghi vào NewsArticle —
/// người duyệt vừa bấm một nút, không nên nhận về một trang lỗi 500 chỉ vì AI trục trặc.
/// </summary>
public class NewsPublisher(
    NewsSiteRepository repository,
    NewsComposeService compose,
    NewsDedupService dedup,
    NewsSiteBuilder builder,
    IOptions<NewsSiteOptions> options,
    ILogger<NewsPublisher> logger)
{
    /// <summary>
    /// XẾP HÀNG viết bài — trả về NGAY, không gọi AI.
    ///
    /// Đo thật: một lượt bấm Duyệt tốn 38 giây, trong đó 37,5 giây là ĐÚNG MỘT lượt gọi AI
    /// viết bài 600 từ. Không rút ngắn được lượt gọi đó. Nhưng người duyệt không có lý do gì
    /// phải ngồi nhìn nó — 18 tin đang chờ là 11 phút nhìn vòng xoay, từng cái một.
    ///
    /// Ở đây chỉ ghi một bản NewsArticle trạng thái Composing rồi trả về. ContentCrawlWorker
    /// nhặt lên viết ở nền và bắn thông báo khi xong.
    /// </summary>
    public async Task<NewsArticleModel> QueueAsync(
        CrawledArticleModel crawled, CancellationToken ct = default)
    {
        var existing = await repository.GetByCrawledAsync(crawled.Id, ct);
        if (existing is not null) return existing;

        var article = new NewsArticleModel
        {
            Id = Guid.NewGuid(),
            CrawledArticleId = crawled.Id,
            CreatedAt = DateTime.UtcNow,
            Status = NewsArticleStatus.Composing,
            // Slug là cột unique nên phải có giá trị ngay. Slug thật sinh từ tít AI viết ra,
            // lúc viết xong — đặt theo tít gốc bây giờ là đóng băng nhầm chuỗi.
            Slug = $"cho-{crawled.Id:N}"[..24],
            Title = crawled.Title ?? "",
        };

        await repository.SaveAsync(article, ct);
        logger.LogInformation("Xếp hàng viết bài cho tin {Id}", crawled.Id);
        return article;
    }

    /// <summary>
    /// Bài chờ viết, GỒM CẢ bài hỏng đáng thử lại.
    ///
    /// Vì sao thử lại: AI không tất định. Đo trên 12 bài đã viết, 3 bài (25%) bị bộ soi dữ kiện
    /// chặn vì bịa mốc thời gian — ví dụ viết "6/8" trong khi tư liệu chỉ có "07/08" và "4.8".
    /// Lượt viết sau thường không lặp lại đúng lỗi đó. Không thử lại thì một phần tư số tin đã
    /// duyệt chết vĩnh viễn vì một lỗi ngẫu nhiên, và không ai bấm lại vì trên giao diện nó nằm
    /// im trong mục "chưa lên web được".
    ///
    /// KHÔNG thử lại bài bị chặn do TRÙNG: viết lại bao nhiêu lần thì nó vẫn trùng bài kia.
    /// Chốt lặp là ComposeAttemptCount, tăng mỗi lượt và dừng ở MaxComposeAttempts.
    /// </summary>
    public async Task<List<NewsArticleModel>> GetQueuedAsync(int take, CancellationToken ct = default)
    {
        var queued = await repository.GetByStatusAsync(NewsArticleStatus.Composing, take, ct);
        if (queued.Count >= take) return queued;

        var retry = (await repository.GetByStatusAsync(NewsArticleStatus.Failed, take * 4, ct))
            .Where(x => x.DuplicateOfNewsId is null
                        && x.ComposeAttemptCount < options.Value.MaxComposeAttempts
                        && x.CrawledArticleId is not null)
            .Take(take - queued.Count)
            .ToList();

        if (retry.Count > 0)
            logger.LogInformation("Thử viết lại {N} bài từng hỏng", retry.Count);

        queued.AddRange(retry);
        return queued;
    }

    public async Task<NewsArticleModel?> PublishAsync(
        CrawledArticleModel crawled, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(crawled.SourceUrl))
        {
            logger.LogWarning("Tin {Id} không có link nguồn — không xuất bản", crawled.Id);
            return null;
        }

        var existing = await repository.GetByCrawledAsync(crawled.Id, ct);
        if (existing is { Status: NewsArticleStatus.Published })
        {
            logger.LogInformation("Tin {Id} đã có bài trên web, bỏ qua", crawled.Id);
            return existing;
        }

        var article = existing ?? new NewsArticleModel
        {
            Id = Guid.NewGuid(),
            CrawledArticleId = crawled.Id,
            CreatedAt = DateTime.UtcNow,
        };

        // Chốt chặn lặp vô hạn, cùng khuôn DedupAttemptCount của luồng cào: quá số lần thì
        // dừng hẳn kèm ghi chú, không để một tin hỏng đốt lượt gọi AI mãi mãi.
        article.ComposeAttemptCount++;
        if (article.ComposeAttemptCount > options.Value.MaxComposeAttempts)
        {
            await FailAsync(article, $"Thử viết {article.ComposeAttemptCount - 1} lần đều hỏng", ct);
            return null;
        }

        var composed = await compose.ComposeAsync(crawled, ct);
        if (composed is null)
        {
            await FailAsync(article, "AI không viết được bài", ct);
            return null;
        }

        if (composed.FactWarnings.Count > 0)
        {
            var why = "Bịa dữ kiện: " + string.Join(" · ", composed.FactWarnings.Take(5));
            await FailAsync(article, why, ct);
            logger.LogWarning("Từ chối xuất bản tin {Id} — {Why}", crawled.Id, why);
            return null;
        }

        // Chống trùng chạy SAU khi AI viết xong, TRƯỚC khi ghi và build.
        //
        // Phải sau khi viết vì thứ cần đem so là tít và sapo AI viết ra — đó mới là chữ độc
        // giả đọc trên trang chủ. So tít gốc của báo là so nhầm văn bản: đo trên hai bài đang
        // nằm trên trang được 0,688, trong khi tít gốc của chúng chồng lấp thấp hơn nhiều.
        //
        // Phải trước khi ghi vì ghi rồi mới phát hiện thì bài đã có slug, đã vào sitemap, và
        // lượt build kế tiếp đẩy nó lên trang thật.
        var dup = await dedup.CheckAsync(composed, article.Id, ct);
        if (dup.IsDuplicate)
        {
            article.DuplicateOfNewsId = dup.OfNewsId;
            article.EventKey = composed.EventKey;
            await FailAsync(article, dup.Reason ?? "Trùng bài đã có trên web", ct);
            logger.LogInformation(
                "Không xuất bản tin {Id} — {Reason} (bắt bởi tầng {Method})",
                crawled.Id, dup.Reason, dup.Method);
            return null;
        }

        // Slug sinh ĐÚNG MỘT LẦN rồi đóng băng. Sửa tiêu đề mà slug đổi theo là URL cũ chết,
        // trong khi Facebook vẫn giữ thẻ og trỏ vào URL đó nhiều ngày.
        //
        // "cho-" và "loi-" là slug TẠM: cột Slug là unique nên phải có giá trị ngay lúc xếp
        // hàng hoặc lúc ghi lỗi, trong khi slug thật chỉ có sau khi AI viết xong tít. Thiếu
        // "cho-" trong danh sách này thì mọi bài đi đường xếp hàng giữ nguyên slug tạm — URL
        // ra "tin/cho-6f3a….html", vẫn mở được nên không ai phát hiện.
        if (string.IsNullOrWhiteSpace(article.Slug)
            || article.Slug.StartsWith("loi-") || article.Slug.StartsWith("cho-"))
            article.Slug = await repository.BuildUniqueSlugAsync(composed.Title, ct);

        article.Title = composed.Title;
        article.Sapo = composed.Sapo;
        article.BodyHtml = NewsHtml.ToSafeHtml(composed.BodyPlain);
        article.KeyPointsJson = JsonSerializer.Serialize(composed.KeyPoints);
        article.TimelineJson = composed.Timeline.Count > 0
            ? JsonSerializer.Serialize(composed.Timeline) : null;
        article.ReadMinutes = NewsHtml.ReadMinutes(composed.BodyPlain);
        article.CategorySlug = crawled.CategorySlug ?? NewsTaxonomy.OtherSlug;
        article.CategoryId = crawled.CategoryId;
        article.SourceUrl = crawled.SourceUrl;
        article.SourceName = Uri.TryCreate(crawled.SourceUrl, UriKind.Absolute, out var uri)
            ? uri.Host.Replace("www.", "")
            : null;

        // Ảnh CHỈ nhận khi biết ghi nguồn là báo nào. Không có tên báo thì bỏ ảnh — đăng ảnh
        // người ta mà không ghi nguồn là chuyện khác hẳn với dẫn nguồn tử tế.
        if (!string.IsNullOrWhiteSpace(crawled.ThumbnailUrl) && article.SourceName is not null)
        {
            article.ImageUrl = crawled.ThumbnailUrl;
            article.ImageCredit = $"Ảnh: {article.SourceName}";
        }
        article.EventKey = composed.EventKey;
        article.DuplicateOfNewsId = null;
        article.Status = NewsArticleStatus.Published;
        article.PublishedAt ??= DateTime.UtcNow;
        article.ErrorMessage = null;

        await repository.SaveAsync(article, ct);
        await builder.BuildAsync(ct);

        return article;
    }

    private async Task FailAsync(NewsArticleModel a, string reason, CancellationToken ct)
    {
        a.Status = NewsArticleStatus.Failed;
        a.ErrorMessage = reason;
        // Slug là cột unique nên phải có giá trị; dùng id để chắc chắn không đụng bài khác.
        if (string.IsNullOrWhiteSpace(a.Slug)) a.Slug = $"loi-{a.Id:N}"[..24];
        await repository.SaveAsync(a, ct);
    }
}
