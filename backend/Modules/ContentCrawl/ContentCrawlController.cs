using Backend.Modules.ContentCrawl.Enums;
using Backend.Shared;
using Backend.Shared.OpenClaw;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.ContentCrawl;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ContentCrawlController(
    ContentCrawlRepository repository,
    ContentCrawlPipelineService pipeline,
    HttpArticleFetcher httpFetcher,
    FeedSourceFetcher feedFetcher,
    FeedDiscoveryService feedDiscovery,
    IOpenClawBrowserClient browser,
    Microsoft.Extensions.Options.IOptions<ContentCrawlOptions> crawlOptions,
    ILogger<ContentCrawlController> logger) : ControllerBase
{
    // ── Nguồn cào ───────────────────────────────────────────────────────────

    [HttpGet("sources")]
    public async Task<IActionResult> GetSources([FromQuery] bool onlyActive = false, CancellationToken ct = default)
    {
        var sources = await repository.GetSourcesAsync(onlyActive, ct);
        return Ok(ApiResponse.Ok(sources.Select(ContentCrawlRepository.ToResponse).ToList()));
    }

    /// <summary>
    /// Dò feed RSS từ địa chỉ trang báo. KHÔNG ghi gì vào DB.
    ///
    /// Giao diện gọi cái này ngay khi người dùng dán địa chỉ, để họ thấy trước "tìm được feed
    /// X, đọc thử ra 50 bài" rồi mới bấm Lưu — thay vì lưu xong mới biết sai.
    /// </summary>
    [HttpPost("sources/discover")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> DiscoverFeed([FromBody] CreateCrawlSourceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Chưa nhập địa chỉ trang"));

        var r = await feedDiscovery.DiscoverAsync(request.Url, ct);
        if (r.FeedUrl is null)
            return Ok(ApiResponse.Ok(new { found = false, tried = r.Tried },
                $"Không tìm được feed RSS ở trang này (đã thử {r.Tried.Count} địa chỉ). "
                + "Trang có thể không có RSS — thử dán địa chỉ chuyên mục cụ thể, ví dụ "
                + "https://tuoitre.vn/giao-duc.htm"));

        return Ok(ApiResponse.Ok(new { found = true, feedUrl = r.FeedUrl, itemCount = r.ItemCount, how = r.How },
            $"{r.How} — đọc thử được {r.ItemCount} bài"));
    }

    [HttpPost("sources")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> CreateSource([FromBody] CreateCrawlSourceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Tên và URL không được để trống"));

        // Nguồn Google News nhận TỪ KHOÁ chứ không phải địa chỉ — bắt nó qua Uri.TryCreate
        // thì không bao giờ tạo được nguồn loại này.
        if (request.SourceType == CrawlSourceType.GoogleNews)
        {
            if (request.Url.Trim().Length < 3)
                return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Từ khoá quá ngắn"));
            var gnews = await repository.CreateSourceAsync(request, ct);
            return Ok(ApiResponse.Ok(ContentCrawlRepository.ToResponse(gnews), "Đã thêm nguồn cào"));
        }

        if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri))
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "URL không hợp lệ"));

        // TỰ DÒ FEED khi người dùng dán địa chỉ trang thường.
        //
        // Người làm nội dung không có nghĩa vụ biết RSS là gì, càng không phải mở mã nguồn
        // trang đi tìm. Họ dán https://tuoitre.vn/giao-duc.htm là đủ.
        //
        // Dò được thì lưu ĐỊA CHỈ FEED và đặt loại Rss. Không dò được thì vẫn lưu như cũ —
        // WebPage cào trang danh mục vẫn chạy, chỉ kém hơn.
        if (request.SourceType != CrawlSourceType.GoogleNews)
        {
            var found = await feedDiscovery.DiscoverAsync(request.Url, ct);
            if (found.FeedUrl is not null)
            {
                request.Url = found.FeedUrl;
                request.SourceType = CrawlSourceType.Rss;
                logger.LogInformation("Tự dò ra feed {Feed} cho {Name} ({How})",
                    found.FeedUrl, request.Name, found.How);
            }
            else
            {
                logger.LogInformation("Không dò ra feed cho {Name}, giữ nguyên cào trang", request.Name);
            }
        }

        // Chặn NGAY lúc tạo nguồn, kèm lời giải thích.
        //
        // Trước đây tạo được nguồn fanpage rồi nó ném NotSupportedException mỗi 30 phút, ghi
        // lượt cào trắng vào lịch sử, và không ai biết. Thà từ chối thẳng ở đây.
        if (uri.Host.Contains("facebook.com", StringComparison.OrdinalIgnoreCase))
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR",
                "Chưa cào được fanpage Facebook. Hệ thống đang lấy tin từ RSS và trang báo; "
                + "cào fanpage cần trình duyệt thật nên để giai đoạn sau."));

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
            // Cào thử chỉ vài bài — mỗi bài là một lượt tải trang thật nên đừng để nhiều.
            //
            // DÙNG ĐÚNG LOẠI NGUỒN người dùng chọn. Trước đây chỗ này viết cứng WebPage và gọi
            // thẳng OpenClaw — tức "Cào thử" chạy một đường mà production KHÔNG còn đi: thử
            // một feed RSS thì nó đem URL feed vào bộ bóc HTML, còn OpenClaw thì mặc định tắt
            // nên luôn về tay không. Người dùng kết luận "feed hỏng" trong khi feed vẫn tốt.
            var probe = new CrawlSourceModel
            {
                Name = "test",
                Url = request.Url.Trim(),
                SourceType = request.SourceType,
                MaxItemsPerRun = Math.Clamp(request.MaxItemsPerRun, 1, 5),
            };

            // Cào thử KHÔNG lọc bài đã cào — mục đích là xem bộ bóc có chạy trên báo này không,
            // nên cần thấy kết quả kể cả với bài đã có trong kho.
            var items = probe.SourceType is CrawlSourceType.Rss or CrawlSourceType.GoogleNews
                ? await feedFetcher.FetchAsync(probe, null, ct)
                : await httpFetcher.FetchAsync(probe, null, ct);
            return Ok(ApiResponse.Ok(new TestCrawlSourceResult
            {
                Ok = true,
                ItemCount = items.Count,
                Items = items.Select(i => new TestCrawlItem
                {
                    Title = i.Title,
                    Summary = i.Summary,
                    ContentLength = i.Content?.Length ?? 0,
                    ContentPreview = i.Content is { Length: > 0 }
                        ? i.Content[..Math.Min(300, i.Content.Length)] : null,
                    Link = i.Link,
                    Author = i.Author,
                    ThumbnailUrl = i.ThumbnailUrl,
                    PublishedAtUtc = i.PublishedAtUtc,
                }).ToList()
            }, $"Bóc được {items.Count} bài toàn văn"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cào thử {Url} thất bại", request.Url);
            return Ok(ApiResponse.Ok(new TestCrawlSourceResult { Ok = false, Error = ex.Message },
                "Không đọc được feed"));
        }
    }

    /// <summary>Kiểm tra OpenClaw có sẵn sàng không — luôn trả 200, không bao giờ 500.</summary>
    [HttpGet("openclaw/ping")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> PingOpenClaw(CancellationToken ct)
    {
        if (!browser.IsConfigured)
            return Ok(ApiResponse.Ok(new { ok = false, reason = "Chưa bật OpenClaw:Enabled hoặc thiếu OpenClaw:Token" },
                "OpenClaw chưa cấu hình"));

        var ok = await browser.PingAsync(ct);
        return Ok(ApiResponse.Ok(new
        {
            ok,
            reason = ok ? null : "Không kết nối được browser control. Kiểm tra: gateway đang chạy, "
                + "browser đã start (`openclaw browser start`), và biến "
                + "OPENCLAW_EAGER_BROWSER_CONTROL_SERVER=1 còn trong service-env."
        }, ok ? "OpenClaw sẵn sàng" : "OpenClaw không phản hồi"));
    }

    /// <summary>
    /// Trình duyệt OpenClaw đã đăng nhập Facebook chưa. Cào fanpage bắt buộc phải có phiên
    /// đăng nhập, nên hỏi trước để báo cho người dùng thay vì để họ tạo nguồn rồi mới lỗi.
    /// </summary>
    [HttpGet("openclaw/facebook-status")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> FacebookStatus(CancellationToken ct)
    {
        if (!browser.IsConfigured)
            return Ok(ApiResponse.Ok(new { loggedIn = false, reason = "OpenClaw chưa cấu hình" }));

        try
        {
            await browser.NavigateAsync("https://www.facebook.com/", ct);
            var eval = await browser.EvaluateAsync(
                """
                () => ({
                  hasLoginForm: !!document.querySelector('input[name="pass"]'),
                  hasSession: document.cookie.includes('c_user')
                })
                """, ct);

            var hasLogin = eval.Result.TryGetProperty("hasLoginForm", out var l) && l.GetBoolean();
            var hasSession = eval.Result.TryGetProperty("hasSession", out var s) && s.GetBoolean();
            var loggedIn = hasSession && !hasLogin;

            return Ok(ApiResponse.Ok(new
            {
                loggedIn,
                reason = loggedIn ? null
                    : "Trình duyệt chưa đăng nhập Facebook. Chạy `openclaw browser open https://facebook.com` "
                      + "rồi đăng nhập bằng tài khoản phụ — phiên sẽ được giữ lại cho các lần cào sau."
            }, loggedIn ? "Đã đăng nhập Facebook" : "Chưa đăng nhập Facebook"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kiểm tra đăng nhập Facebook thất bại");
            return Ok(ApiResponse.Ok(new { loggedIn = false, reason = ex.Message }, "Không kiểm tra được"));
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
        catch (InvalidOperationException ex)
        {
            // Nguồn đang được cào bởi người khác (hoặc bởi bot Telegram). Đây là chuyện bình
            // thường, không phải sự cố — trả 409 kèm câu giải thích thay vì 500 INTERNAL_ERROR
            // khiến người dùng tưởng hệ thống hỏng rồi bấm lại lần nữa.
            return Conflict(ApiResponse.Fail("CRAWL_IN_PROGRESS", ex.Message));
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
        // Cấp số cho tin chưa có, để web và Telegram luôn nói cùng một hệ mã. Rẻ: chỉ ghi
        // khi thực sự có tin thiếu số, còn lại là một truy vấn đọc.
        await repository.EnsureShortCodesAsync(ct);

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
            TwoGateFlow = crawlOptions.Value.TwoGateFlow,
        }));
    }

    /// <summary>
    /// Cứu tin đã mất: bài bị đánh trùng với một bài mà bài đó sau lại bị lọc.
    /// Chạy lại được nhiều lần.
    /// </summary>
    [HttpPost("articles/recover-lost")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> RecoverLost(CancellationToken ct)
    {
        var n = await pipeline.RecoverLostDuplicatesAsync(ct);
        return Ok(ApiResponse.Ok(new { recovered = n }, $"Đã đưa {n} tin quay lại hàng xử lý"));
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
