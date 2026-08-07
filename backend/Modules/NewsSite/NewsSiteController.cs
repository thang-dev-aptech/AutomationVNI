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
    NewsPublisher publisher,
    NewsSiteBuilder builder) : ControllerBase
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

    /// <summary>Viết bài website từ một tin đã cào rồi dựng lại trang. CỬA 1.</summary>
    [HttpPost("compose/{crawledArticleId:guid}")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Compose(Guid crawledArticleId, CancellationToken ct)
    {
        var crawled = await context.Set<CrawledArticleModel>()
            .FirstOrDefaultAsync(x => x.Id == crawledArticleId && !x.IsDeleted, ct);
        if (crawled is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy tin"));

        var news = await publisher.PublishAsync(crawled, ct);
        if (news is null)
        {
            var failed = await repository.GetByCrawledAsync(crawled.Id, ct);
            return BadRequest(ApiResponse.Fail("COMPOSE_FAILED",
                failed?.ErrorMessage ?? "Không viết được bài"));
        }

        return Ok(ApiResponse.Ok(ToResponse(news), "Đã lên web"));
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
