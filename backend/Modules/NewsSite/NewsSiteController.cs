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

public class SetSubscriberActiveRequest
{
    public bool IsActive { get; set; }
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
    Microsoft.Extensions.Options.IOptions<NewsSiteOptions> newsOptions,
    NewsSiteBuilder builder) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? category, [FromQuery] int size = 50,
        CancellationToken ct = default)
    {
        var items = await repository.GetForAdminAsync(category, size, ct);

        // Điểm chất lượng nằm ở tin GỐC, không ở bài web. Nạp một lượt cho cả danh sách thay
        // vì mỗi bài một truy vấn.
        var crawledIds = items.Where(x => x.CrawledArticleId.HasValue)
            .Select(x => x.CrawledArticleId!.Value).ToList();
        var scores = await context.Set<CrawledArticleModel>()
            .Where(x => crawledIds.Contains(x.Id))
            .Select(x => new { x.Id, x.QualityScore })
            .ToDictionaryAsync(x => x.Id, x => x.QualityScore, ct);

        var suggestMin = newsOptions.Value.FanpageSuggestMinScore;
        return Ok(ApiResponse.Ok(items.Select(a =>
        {
            var score = a.CrawledArticleId is Guid cid && scores.TryGetValue(cid, out var s) ? s : null;
            return (object)new
            {
                a.Id, a.Slug, a.Title, a.Sapo, a.CategorySlug, a.ReadMinutes,
                a.SourceName, a.SourceUrl, status = a.Status.ToString(),
                a.PublishedAt, a.ViewCount, a.ErrorMessage, a.EventKey,
                // Nút "Xem trên web" bấm vào ĐÂY — phải là địa chỉ mở được ngay. Trả địa chỉ
                // công khai khi tên miền chưa sống thì bấm ra lỗi DNS.
                url = repository.PreviewUrlOf(a.Slug),
                publicUrl = repository.PublicUrlOf(a.Slug),
                qualityScore = score,
                // Gợi ý, không phải luật. Người duyệt vẫn đăng được bài điểm thấp hơn.
                suggestFanpage = score >= suggestMin,
            };
        }).ToList()));
    }

    /// <summary>Trạng thái thư mục xuất bản — kiểm trước khi tìm lỗi ở chỗ khác.</summary>
    [HttpGet("status")]
    public IActionResult Status()
    {
        var ok = builder.CanWrite(out var reason);
        var opt = newsOptions.Value;
        return Ok(ApiResponse.Ok(new
        {
            canWrite = ok,
            path = reason,
            publicBaseUrl = opt.PublicBaseUrl,
            previewBaseUrl = opt.PreviewBaseUrl,
            // Đang xem thử ở địa chỉ khác địa chỉ công khai — giao diện cần nói rõ, nếu không
            // người duyệt tưởng bài đã sống trên tên miền thật.
            isPreviewOnly = !string.IsNullOrWhiteSpace(opt.PreviewBaseUrl)
                            && opt.PreviewBaseUrl != opt.PublicBaseUrl,
        }));
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

    /// <summary>
    /// Bù ảnh cho bài lên web trước khi có tính năng ảnh.
    ///
    /// Lấy ThumbnailUrl của tin gốc. CHỈ nhận khi biết tên báo — đăng ảnh người ta mà không
    /// ghi nguồn là chuyện khác hẳn với dẫn nguồn tử tế, đúng luật đã đặt ở NewsPublisher.
    /// Chạy được nhiều lần, chỉ đụng bài còn thiếu.
    /// </summary>
    [HttpPost("backfill-images")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> BackfillImages(CancellationToken ct)
    {
        var articles = (await repository.GetPublishedAsync(null, 200, ct))
            .Where(x => string.IsNullOrWhiteSpace(x.ImageUrl) && x.CrawledArticleId is not null)
            .ToList();

        var ids = articles.Select(x => x.CrawledArticleId!.Value).ToList();
        var thumbs = await context.Set<CrawledArticleModel>()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.ThumbnailUrl })
            .ToDictionaryAsync(x => x.Id, x => x.ThumbnailUrl, ct);

        var filled = 0;
        var skipped = new List<string>();
        foreach (var a in articles)
        {
            var thumb = thumbs.GetValueOrDefault(a.CrawledArticleId!.Value);
            if (string.IsNullOrWhiteSpace(thumb)) { skipped.Add($"{a.Title}: tin gốc không có ảnh"); continue; }
            if (string.IsNullOrWhiteSpace(a.SourceName)) { skipped.Add($"{a.Title}: không rõ báo nào"); continue; }

            a.ImageUrl = thumb;
            a.ImageCredit = $"Ảnh: {a.SourceName}";
            await repository.SaveAsync(a, ct);
            filled++;
        }

        if (filled > 0) await builder.BuildAsync(ct);

        return Ok(ApiResponse.Ok(new { filled, skipped },
            $"Đã bù ảnh cho {filled} bài" + (skipped.Count > 0 ? $", bỏ qua {skipped.Count}" : "")));
    }

    [HttpPost("build")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Build(CancellationToken ct)
        => Ok(ApiResponse.Ok(await builder.BuildAsync(ct), "Đã dựng lại trang tin"));

    [HttpPost("{id:guid}/resend-newsletter")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> ResendNewsletter(Guid id, CancellationToken ct)
    {
        var ok = await repository.ResetNewsletterSentAsync(id, ct);
        if (!ok) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));

        return Ok(ApiResponse.Ok(new { id }, "Sẽ gửi lại trong tối đa 60 giây"));
    }

    [HttpGet("subscribers")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Subscribers(
        [FromQuery] string? keyword, [FromQuery] bool? isActive,
        [FromQuery] int index = 1, [FromQuery] int size = 20, CancellationToken ct = default)
    {
        var result = await repository.GetSubscribersForAdminAsync(keyword, isActive, index, size, ct);
        return Ok(ApiResponse.Ok(new
        {
            items = result.Items.Select(s => (object)new
            {
                s.Id, s.Email, s.IsActive, s.CreatedAt, s.UnsubscribedAt,
            }),
            result.Total,
            result.Index,
            result.Size,
        }));
    }

    [HttpPatch("subscribers/{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> SetSubscriberActive(
        Guid id, [FromBody] SetSubscriberActiveRequest request, CancellationToken ct)
    {
        var sub = await repository.SetSubscriberActiveAsync(id, request.IsActive, ct);
        if (sub is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy người đăng ký"));

        return Ok(ApiResponse.Ok(new { sub.Id, sub.Email, sub.IsActive, sub.UnsubscribedAt },
            request.IsActive ? "Đã bật lại nhận tin" : "Đã tắt nhận tin"));
    }

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
        url = repository.PreviewUrlOf(a.Slug),
        publicUrl = repository.PublicUrlOf(a.Slug),
    };
}
