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
    NewsSiteBuilder builder,
    IOptions<NewsSiteOptions> options,
    ILogger<NewsPublisher> logger)
{
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

        // Slug sinh ĐÚNG MỘT LẦN rồi đóng băng. Sửa tiêu đề mà slug đổi theo là URL cũ chết,
        // trong khi Facebook vẫn giữ thẻ og trỏ vào URL đó nhiều ngày.
        if (string.IsNullOrWhiteSpace(article.Slug) || article.Slug.StartsWith("loi-"))
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
