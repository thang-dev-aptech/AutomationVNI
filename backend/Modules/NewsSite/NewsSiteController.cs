using System.Text.Json;
using Backend.Data;
using Backend.Modules.ContentCrawl;
using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.NewsSite;

/// <summary>
/// Quản lý trang tin: xem bài đã lên web, viết bài từ tin đã cào, dựng lại trang.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NewsSiteController(
    AppDbContext context,
    NewsSiteRepository repository,
    NewsComposeService compose,
    NewsSiteBuilder builder,
    ILogger<NewsSiteController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? category, [FromQuery] int size = 50,
        CancellationToken ct = default)
    {
        var items = await repository.GetPublishedAsync(category, size, ct);
        return Ok(ApiResponse.Ok(items.Select(ToResponse).ToList()));
    }

    /// <summary>Trạng thái thư mục xuất bản — kiểm trước khi tìm lỗi ở chỗ khác.</summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        var ok = builder.CanWrite(out var reason);
        return Ok(ApiResponse.Ok(new { canWrite = ok, path = reason }));
    }

    /// <summary>
    /// Viết bài website từ một tin đã cào, rồi dựng lại trang.
    /// Chưa nối vào luồng duyệt — mốc 8 sẽ gọi hàm này từ ApproveAsync.
    /// </summary>
    [HttpPost("compose/{crawledArticleId:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Compose(Guid crawledArticleId, CancellationToken ct)
    {
        var crawled = await context.Set<CrawledArticleModel>()
            .FirstOrDefaultAsync(x => x.Id == crawledArticleId && !x.IsDeleted, ct);
        if (crawled is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy tin"));

        // Dẫn nguồn là điều kiện xuất bản, không phải tuỳ chọn. Chặn ở đây chứ không chỉ dặn
        // trong prompt — bài không xác định được nguồn thì không đăng.
        if (string.IsNullOrWhiteSpace(crawled.SourceUrl))
            return BadRequest(ApiResponse.Fail("NO_SOURCE", "Tin không có link nguồn — không xuất bản"));

        var existing = await repository.GetByCrawledAsync(crawled.Id, ct);
        if (existing is { Status: NewsArticleStatus.Published })
            return Ok(ApiResponse.Ok(ToResponse(existing), "Tin này đã có bài trên web"));

        var article = existing ?? new NewsArticleModel
        {
            Id = Guid.NewGuid(),
            CrawledArticleId = crawled.Id,
            CreatedAt = DateTime.UtcNow,
        };

        article.ComposeAttemptCount++;
        var composed = await compose.ComposeAsync(crawled, ct);

        if (composed is null)
        {
            article.Status = NewsArticleStatus.Failed;
            article.ErrorMessage = "AI không viết được bài";
            // Slug phải có vì cột unique; dùng tạm id để không đụng bài khác.
            if (string.IsNullOrWhiteSpace(article.Slug)) article.Slug = $"loi-{article.Id:N}"[..24];
            await repository.SaveAsync(article, ct);
            return StatusCode(502, ApiResponse.Fail("COMPOSE_FAILED", "AI không viết được bài, thử lại sau"));
        }

        if (composed.FactWarnings.Count > 0)
        {
            article.Status = NewsArticleStatus.Failed;
            article.ErrorMessage = "Bịa dữ kiện: " + string.Join(" · ", composed.FactWarnings);
            if (string.IsNullOrWhiteSpace(article.Slug)) article.Slug = $"loi-{article.Id:N}"[..24];
            await repository.SaveAsync(article, ct);

            logger.LogWarning("Từ chối xuất bản tin {Id}: {W}", crawled.Id, article.ErrorMessage);
            return BadRequest(ApiResponse.Fail("FABRICATED_FACTS",
                "Bài có dữ kiện không có trong tư liệu gốc, không xuất bản: "
                + string.Join(" · ", composed.FactWarnings.Take(3))));
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
        article.SourceName = crawled.SourceUrl is { } u && Uri.TryCreate(u, UriKind.Absolute, out var uri)
            ? uri.Host.Replace("www.", "") : null;
        article.SourceUrl = crawled.SourceUrl;
        article.Status = NewsArticleStatus.Published;
        article.PublishedAt ??= DateTime.UtcNow;
        article.ErrorMessage = null;

        await repository.SaveAsync(article, ct);
        var build = await builder.BuildAsync(ct);

        return Ok(ApiResponse.Ok(new
        {
            article = ToResponse(article),
            url = repository.PublicUrlOf(article.Slug),
            build,
        }, "Đã viết bài và dựng trang"));
    }

    [HttpPost("build")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Build(CancellationToken ct)
        => Ok(ApiResponse.Ok(await builder.BuildAsync(ct), "Đã dựng lại trang tin"));

    private object ToResponse(NewsArticleModel a) => new
    {
        a.Id,
        a.Slug,
        a.Title,
        a.Sapo,
        a.CategorySlug,
        a.ReadMinutes,
        a.SourceName,
        a.SourceUrl,
        status = a.Status.ToString(),
        a.PublishedAt,
        a.ViewCount,
        a.ErrorMessage,
        url = repository.PublicUrlOf(a.Slug),
    };
}
