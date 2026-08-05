using Backend.Modules.ContentCrawl.Enums;
using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.ContentCrawl;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ContentCrawlController(
    ContentCrawlRepository repository,
    ContentCrawlPipelineService pipeline,
    RssFeedReader rssReader,
    ILogger<ContentCrawlController> logger) : ControllerBase
{
    // ── Nguồn cào ───────────────────────────────────────────────────────────

    [HttpGet("sources")]
    public async Task<IActionResult> GetSources([FromQuery] bool onlyActive = false, CancellationToken ct = default)
    {
        var sources = await repository.GetSourcesAsync(onlyActive, ct);
        return Ok(ApiResponse.Ok(sources.Select(ContentCrawlRepository.ToResponse).ToList()));
    }

    [HttpPost("sources")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> CreateSource([FromBody] CreateCrawlSourceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Tên và URL không được để trống"));
        if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out _))
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "URL không hợp lệ"));

        var entity = await repository.CreateSourceAsync(request, ct);
        return Ok(ApiResponse.Ok(ContentCrawlRepository.ToResponse(entity), "Đã thêm nguồn cào"));
    }

    [HttpPut("sources/{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> UpdateSource(Guid id, [FromBody] UpdateCrawlSourceRequest request, CancellationToken ct)
    {
        var entity = await repository.UpdateSourceAsync(id, request, ct);
        return entity is null
            ? NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy nguồn cào"))
            : Ok(ApiResponse.Ok(ContentCrawlRepository.ToResponse(entity), "Đã cập nhật"));
    }

    [HttpDelete("sources/{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> DeleteSource(Guid id, CancellationToken ct)
        => await repository.SoftDeleteSourceAsync(id, ct)
            ? Ok(ApiResponse.Ok("Đã xoá nguồn cào"))
            : NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy nguồn cào"));

    /// <summary>
    /// Cào thử: fetch + parse thật nhưng KHÔNG ghi gì vào DB. Đây là cách debug một feed
    /// mà không làm bẩn dữ liệu — đáng giá 20 dòng của nó.
    /// </summary>
    [HttpPost("sources/test")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> TestSource([FromBody] CreateCrawlSourceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "URL không được để trống"));

        try
        {
            var items = await rssReader.FetchAsync(request.Url.Trim(), Math.Clamp(request.MaxItemsPerRun, 1, 20), ct);
            return Ok(ApiResponse.Ok(new TestCrawlSourceResult
            {
                Ok = true,
                ItemCount = items.Count,
                Items = items.Select(i => new TestCrawlItem
                {
                    Title = i.Title,
                    Summary = i.Summary,
                    Link = i.Link,
                    Author = i.Author,
                    ThumbnailUrl = i.ThumbnailUrl,
                    PublishedAtUtc = i.PublishedAtUtc,
                }).ToList()
            }, $"Đọc được {items.Count} bài"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cào thử {Url} thất bại", request.Url);
            return Ok(ApiResponse.Ok(new TestCrawlSourceResult { Ok = false, Error = ex.Message },
                "Không đọc được feed"));
        }
    }

    [HttpPost("sources/{id:guid}/crawl-now")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> CrawlNow(Guid id, [FromBody] CrawlNowRequest? request, CancellationToken ct)
    {
        try
        {
            var run = await pipeline.RunSourceAsync(id, request?.TriggerSource ?? "manual", ct);
            var processed = await pipeline.ProcessPendingAsync(ct);
            return Ok(ApiResponse.Ok(ContentCrawlRepository.ToResponse(run),
                $"Cào xong: {run.ItemsNew} bài mới, {run.ItemsDuplicate} trùng, {run.ItemsFiltered} bị lọc; đã xử lý {processed} tin"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
    }

    // ── Lượt cào ────────────────────────────────────────────────────────────

    [HttpGet("runs")]
    public async Task<IActionResult> GetRuns([FromQuery] Guid? sourceId, [FromQuery] int take = 30, CancellationToken ct = default)
    {
        var runs = await repository.GetRecentRunsAsync(sourceId, take, ct);
        var sources = await repository.GetSourcesAsync(false, ct);
        var nameById = sources.ToDictionary(s => s.Id, s => s.Name);
        return Ok(ApiResponse.Ok(runs
            .Select(r => ContentCrawlRepository.ToResponse(r, nameById.GetValueOrDefault(r.CrawlSourceId)))
            .ToList()));
    }

    // ── Tin đã cào ──────────────────────────────────────────────────────────

    [HttpPost("articles/filter")]
    public async Task<IActionResult> FilterArticles([FromBody] CrawledArticleFilterRequest request, CancellationToken ct)
    {
        var paged = await repository.FilterArticlesAsync(request, ct);
        var sources = await repository.GetSourcesAsync(false, ct);
        var nameById = sources.ToDictionary(s => s.Id, s => s.Name);

        return Ok(ApiResponse.Ok(new PagedResult<CrawledArticleResponse>
        {
            Items = paged.Items
                .Select(a => ContentCrawlRepository.ToResponse(a, nameById.GetValueOrDefault(a.CrawlSourceId)))
                .ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size,
        }));
    }

    [HttpGet("articles/{id:guid}")]
    public async Task<IActionResult> GetArticle(Guid id, CancellationToken ct)
    {
        var article = await repository.GetByIdAsync(id, ct);
        if (article is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy tin"));
        var source = await repository.GetSourceAsync(article.CrawlSourceId, ct);
        return Ok(ApiResponse.Ok(ContentCrawlRepository.ToResponse(article, source?.Name)));
    }

    [HttpGet("articles/summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var byStatus = await repository.CountByStatusAsync(ct);
        var sources = await repository.GetSourcesAsync(true, ct);
        return Ok(ApiResponse.Ok(new CrawlInboxSummaryResponse
        {
            ByStatus = byStatus.ToDictionary(x => x.Key.ToString(), x => x.Value),
            PendingCount = byStatus.GetValueOrDefault(CrawledArticleStatus.Pending),
            TotalActiveSources = sources.Count,
            LastRunAt = sources.Max(s => s.LastRunAt),
        }));
    }

    [HttpPost("articles/{id:guid}/approve")]
    [Authorize(Roles = "Admin,Reviewer")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveCrawledArticleRequest request, CancellationToken ct)
    {
        try
        {
            var result = await pipeline.ApproveAsync(id, request ?? new ApproveCrawledArticleRequest(), ct);
            return Ok(ApiResponse.Ok(result, result.Message));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message)); }
        catch (ArgumentException ex) { return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", ex.Message)); }
    }

    [HttpPost("articles/{id:guid}/reject")]
    [Authorize(Roles = "Admin,Reviewer")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectCrawledArticleRequest? request, CancellationToken ct)
    {
        try
        {
            var article = await pipeline.RejectAsync(id, request?.Reason, ct);
            return Ok(ApiResponse.Ok(ContentCrawlRepository.ToResponse(article), "Đã loại tin"));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message)); }
    }

    [HttpPost("articles/{id:guid}/not-duplicate")]
    [Authorize(Roles = "Admin,Reviewer")]
    public async Task<IActionResult> NotDuplicate(Guid id, CancellationToken ct)
    {
        try
        {
            var article = await pipeline.MarkNotDuplicateAsync(id, ct);
            return Ok(ApiResponse.Ok(ContentCrawlRepository.ToResponse(article), "Đã bỏ đánh dấu trùng"));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message)); }
    }

    [HttpPost("articles/{id:guid}/rededup")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Rededup(Guid id, CancellationToken ct)
    {
        try
        {
            await pipeline.RededupAsync(id, ct);
            var processed = await pipeline.ProcessPendingAsync(ct);
            var article = await repository.GetByIdAsync(id, ct);
            return Ok(ApiResponse.Ok(
                article is null ? null : ContentCrawlRepository.ToResponse(article),
                $"Đã chấm lại ({processed} tin được xử lý)"));
        }
        catch (KeyNotFoundException ex) { return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message)); }
    }
}
