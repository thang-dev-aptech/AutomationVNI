using Backend.Data;
using Backend.Modules.ContentCrawl;
using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.NewsSite;

public class PublishFanpageRequest
{
    public List<Guid>? ChannelIds { get; set; }
    /// <summary>Đăng ngay thay vì dừng ở Approved chờ lên lịch tay.</summary>
    public bool AutoPublish { get; set; } = true;
}

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
    NewsFanpageService fanpage,
    NewsDedupService dedup,
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

    /// <summary>CỬA 2 — đăng bài đã lên web sang fanpage.</summary>
    [HttpPost("{id:guid}/fanpage")]
    [Authorize(Roles = "Admin,Reviewer,ContentManager")]
    public async Task<IActionResult> ToFanpage(
        Guid id, [FromBody] PublishFanpageRequest request, CancellationToken ct)
    {
        var news = await repository.GetAsync(id, ct);
        if (news is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài"));

        try
        {
            var result = await fanpage.PublishAsync(
                news, request.ChannelIds ?? [], request.AutoPublish, ct);
            return Ok(ApiResponse.Ok(result,
                $"Đã tạo {result.Created} bài cho: {string.Join(", ", result.Channels)}"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Bù khoá sự việc cho bài đã lên web từ trước khi có chống trùng ở khâu xuất bản.
    /// Chạy được nhiều lần, chỉ đụng bài còn thiếu.
    /// </summary>
    [HttpPost("backfill-event-keys")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> BackfillEventKeys(CancellationToken ct)
    {
        var n = await dedup.BackfillEventKeysAsync(ct);
        return Ok(ApiResponse.Ok(new { filled = n }, $"Đã bù khoá sự việc cho {n} bài"));
    }

    /// <summary>
    /// Gỡ một bài khỏi website. Không xoá dữ liệu — chỉ chuyển trạng thái rồi dựng lại trang.
    ///
    /// Giữ bản ghi thay vì xoá vì slug phải ở lại: URL đã ra ngoài, Facebook đã cache thẻ og.
    /// Xoá hẳn thì slug đó có thể bị bài khác chiếm, và link cũ dẫn sang bài không liên quan.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken ct)
    {
        var news = await repository.GetAsync(id, ct);
        if (news is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài"));

        // Hidden chứ không xoá: file HTML ở lại trên đĩa nên URL cũ vẫn mở được, chỉ biến
        // khỏi trang chủ, trang chuyên mục và sitemap. Link đã chia sẻ lên Facebook không chết.
        news.Status = NewsArticleStatus.Hidden;
        news.ErrorMessage = "Đã gỡ khỏi web bằng tay";
        await repository.SaveAsync(news, ct);
        await builder.BuildAsync(ct);

        return Ok(ApiResponse.Ok(new { id }, "Đã gỡ khỏi web"));
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
