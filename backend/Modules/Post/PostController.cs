using Backend.Modules.Post.Enums;
using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Post;

[ApiController]
[Route("api/[controller]")]
public class PostController
    : BaseController<PostModel, PostRepository,
        CreatePostRequest, UpdatePostRequest,
        PostFilterRequest, PostResponse>
{
    private readonly PostRepository _repo;
    private readonly PostWorkflowService _workflow;
    private readonly GenerationJob.GenerationJobPipelineService _generationPipeline;
    private readonly PublishLog.IPublishPipelineService _publishPipeline;

    public PostController(
        PostRepository repository,
        PostWorkflowService workflow,
        GenerationJob.GenerationJobPipelineService generationPipeline,
        PublishLog.IPublishPipelineService publishPipeline) : base(repository)
    {
        _repo = repository;
        _workflow = workflow;
        _generationPipeline = generationPipeline;
        _publishPipeline = publishPipeline;
    }

    protected override string EntityLabel => "bài viết";
    protected override PostResponse ToResponse(PostModel e) => PostRepository.ToResponse(e);

    protected override async Task<PostModel> CreateEntityAsync(CreatePostRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("Tiêu đề không được để trống");
        return await _repo.CreateAsync(request, ct);
    }

    protected override Task<PostModel?> UpdateEntityAsync(Guid id, UpdatePostRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<PostResponse>> FilterEntitiesAsync(PostFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    // --- Generation pipeline ---

    [HttpPost("{id:guid}/queue-text-generation")]
    public async Task<IActionResult> QueueTextGeneration(Guid id, CancellationToken ct)
    {
        var post = await _workflow.GetPostAsync(id, ct);
        if (post is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));

        if (!_workflow.IsOwner(post) && !_workflow.IsInAnyRole("Admin", "ContentManager"))
            return StatusCode(403, ApiResponse.Fail("FORBIDDEN", "Bạn không có quyền thực hiện thao tác này"));

        var result = await _generationPipeline.QueueTextGenerationAsync(id, ct);
        return Ok(ApiResponse.Ok(result, "Đã queue job sinh text"));
    }

    [HttpPost("{id:guid}/queue-image-generation")]
    public async Task<IActionResult> QueueImageGeneration(Guid id, CancellationToken ct)
    {
        var post = await _workflow.GetPostAsync(id, ct);
        if (post is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));

        if (!_workflow.IsOwner(post) && !_workflow.IsInAnyRole("Admin", "ContentManager"))
            return StatusCode(403, ApiResponse.Fail("FORBIDDEN", "Bạn không có quyền thực hiện thao tác này"));

        var result = await _generationPipeline.QueueImageGenerationAsync(id, ct);
        return Ok(ApiResponse.Ok(result, "Đã queue job sinh ảnh"));
    }

    [HttpPost("{id:guid}/queue-image-render")]
    public async Task<IActionResult> QueueImageRender(Guid id, CancellationToken ct)
    {
        var post = await _workflow.GetPostAsync(id, ct);
        if (post is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));

        if (!_workflow.IsOwner(post) && !_workflow.IsInAnyRole("Admin", "ContentManager"))
            return StatusCode(403, ApiResponse.Fail("FORBIDDEN", "Bạn không có quyền thực hiện thao tác này"));

        var result = await _generationPipeline.QueueImageRenderAsync(id, ct);
        return Ok(ApiResponse.Ok(result, "Đã queue job render overlay"));
    }

    // --- One-click: tạo bài + sinh text + sinh ảnh + set Approved (bỏ bước duyệt) ---

    [HttpPost("create-and-generate")]
    public async Task<IActionResult> CreateAndGenerate([FromBody] CreatePostRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Ý tưởng không được để trống"));
        if (request.SocialChannelId == Guid.Empty)
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Phải chọn kênh đăng"));

        var post = await _repo.CreateAsync(request, ct);

        try
        {
            await GenerateTextThenImageAsync(post.Id, ct);
            await _workflow.ApproveAsync(post.Id, ct); // bỏ duyệt: xong là Approved, sẵn sàng đăng/lên lịch
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            var partial = await _workflow.GetPostAsync(post.Id, ct);
            return Ok(ApiResponse.Ok(ToResponse(partial!),
                $"Đã tạo bài nhưng sinh nội dung chưa trọn vẹn: {ex.Message}. Có thể tạo lại ở màn preview."));
        }

        var final = await _workflow.GetPostAsync(post.Id, ct);
        return Ok(ApiResponse.Ok(ToResponse(final!), "Đã tạo bài và sinh nội dung xong"));
    }

    [HttpPost("{id:guid}/regenerate-text")]
    public async Task<IActionResult> RegenerateText(Guid id, CancellationToken ct)
    {
        var guard = await EnsureGenerationPermissionAsync(id, ct);
        if (guard is not null) return guard;

        var qt = await _generationPipeline.QueueTextGenerationAsync(id, ct);
        await _generationPipeline.ProcessAsync(qt.JobId, ct);
        await _workflow.ApproveAsync(id, ct);

        var post = await _workflow.GetPostAsync(id, ct);
        return Ok(ApiResponse.Ok(ToResponse(post!), "Đã tạo lại nội dung"));
    }

    [HttpPost("{id:guid}/regenerate-image")]
    public async Task<IActionResult> RegenerateImage(Guid id, CancellationToken ct)
    {
        var guard = await EnsureGenerationPermissionAsync(id, ct);
        if (guard is not null) return guard;

        var qi = await _generationPipeline.QueueImageGenerationAsync(id, ct);
        await _generationPipeline.ProcessAsync(qi.JobId, ct);
        await _workflow.ApproveAsync(id, ct);

        var post = await _workflow.GetPostAsync(id, ct);
        return Ok(ApiResponse.Ok(ToResponse(post!), "Đã tạo lại ảnh"));
    }

    private Task GenerateTextThenImageAsync(Guid postId, CancellationToken ct)
        => _generationPipeline.GenerateForPostAsync(postId, ct);

    // --- Bulk (tạo hàng loạt) ---

    /// <summary>Tạo hàng loạt bài (items × channels). Trả về ngay; worker sinh nội dung nền.</summary>
    [HttpPost("bulk-create")]
    public async Task<IActionResult> BulkCreate([FromBody] BulkCreatePostRequest request, CancellationToken ct)
    {
        var result = await _repo.BulkCreateAsync(request, ct);
        return Ok(ApiResponse.Ok(result,
            $"Đã tạo {result.Created} bài — đang sinh nội dung nền, xem tiến độ ở batch."));
    }

    /// <summary>Duyệt hàng loạt các bài WaitingReview trong batch (hoặc theo postIds).</summary>
    [HttpPost("bulk-approve")]
    public async Task<IActionResult> BulkApprove([FromBody] BulkTargetRequest request, CancellationToken ct)
    {
        var posts = await _repo.ResolveTargetsAsync(request.BatchId, request.PostIds, [PostStatus.WaitingReview], ct);
        var ok = new List<Guid>();
        foreach (var p in posts)
        {
            try { await _workflow.ApproveAsync(p.Id, ct); ok.Add(p.Id); }
            catch { /* bỏ qua bài lỗi trạng thái */ }
        }
        return Ok(ApiResponse.Ok(new BulkOperationResult
        {
            Affected = ok.Count,
            Skipped = posts.Count - ok.Count,
            PostIds = ok,
            Message = $"Đã duyệt {ok.Count}/{posts.Count} bài"
        }));
    }

    /// <summary>Lên lịch hàng loạt các bài Approved, rải theo khung giờ vàng (spread).</summary>
    [HttpPost("bulk-schedule")]
    public async Task<IActionResult> BulkSchedule([FromBody] BulkScheduleRequest request, CancellationToken ct)
    {
        var posts = await _repo.ResolveTargetsAsync(request.BatchId, request.PostIds, [PostStatus.Approved], ct);
        var times = ComputeSlotTimesUtc(request.StartAtUtc ?? DateTime.UtcNow, request.TimeSlots, request.Timezone, posts.Count);

        var ok = new List<Guid>();
        for (var i = 0; i < posts.Count && i < times.Count; i++)
        {
            try { await _workflow.ScheduleAsync(posts[i].Id, times[i], request.Timezone, ct); ok.Add(posts[i].Id); }
            catch { /* bỏ qua bài lỗi */ }
        }
        return Ok(ApiResponse.Ok(new BulkOperationResult
        {
            Affected = ok.Count,
            Skipped = posts.Count - ok.Count,
            PostIds = ok,
            Message = $"Đã lên lịch {ok.Count}/{posts.Count} bài, rải theo khung giờ"
        }));
    }

    /// <summary>Tiến độ 1 batch: tổng + đếm theo trạng thái + danh sách bài.</summary>
    [HttpGet("batch/{batchId:guid}")]
    public async Task<IActionResult> BatchStatus(Guid batchId, CancellationToken ct)
    {
        var posts = await _repo.ResolveTargetsAsync(batchId, null, null, ct);
        var byStatus = posts.GroupBy(p => p.Status)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());
        return Ok(ApiResponse.Ok(new
        {
            batchId,
            total = posts.Count,
            byStatus,
            posts = posts.Select(PostRepository.ToResponse).ToList()
        }));
    }

    /// <summary>Sinh danh sách mốc UTC theo khung giờ (local) rải qua các ngày, chỉ lấy mốc tương lai.</summary>
    private static List<DateTime> ComputeSlotTimesUtc(DateTime startUtc, List<string> slots, string timezone, int count)
    {
        if (count <= 0) return [];
        var tz = ResolveTimeZone(timezone);
        var slotSpans = (slots ?? [])
            .Select(s => TimeSpan.TryParse(s?.Trim(), out var ts) ? ts : (TimeSpan?)null)
            .Where(ts => ts.HasValue).Select(ts => ts!.Value)
            .OrderBy(ts => ts).ToList();
        if (slotSpans.Count == 0)
            slotSpans = [new TimeSpan(8, 0, 0), new TimeSpan(12, 0, 0), new TimeSpan(20, 0, 0)];

        var nowUtc = DateTime.UtcNow.AddMinutes(1);
        var effectiveStartUtc = startUtc > nowUtc ? startUtc : nowUtc;
        var day = TimeZoneInfo.ConvertTimeFromUtc(effectiveStartUtc, tz).Date;

        var result = new List<DateTime>();
        var guard = 0;
        while (result.Count < count && guard++ < 500)
        {
            foreach (var slot in slotSpans)
            {
                var local = DateTime.SpecifyKind(day + slot, DateTimeKind.Unspecified);
                var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
                if (utc > nowUtc)
                {
                    result.Add(utc);
                    if (result.Count >= count) break;
                }
            }
            day = day.AddDays(1);
        }
        return result;
    }

    private static TimeZoneInfo ResolveTimeZone(string timezone)
    {
        foreach (var id in new[] { timezone, "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* thử id kế tiếp */ }
        }
        return TimeZoneInfo.CreateCustomTimeZone("vn-fallback", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    }

    private async Task<IActionResult?> EnsureGenerationPermissionAsync(Guid id, CancellationToken ct)
    {
        var post = await _workflow.GetPostAsync(id, ct);
        if (post is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));
        if (!_workflow.IsOwner(post) && !_workflow.IsInAnyRole("Admin", "ContentManager"))
            return StatusCode(403, ApiResponse.Fail("FORBIDDEN", "Bạn không có quyền thực hiện thao tác này"));
        return null;
    }

    [HttpGet("{id:guid}/generation-status")]
    public async Task<IActionResult> GetGenerationStatus(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await _generationPipeline.GetGenerationStatusAsync(id, ct);
            return Ok(ApiResponse.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));
        }
    }

    // --- Review / publish workflow ---

    [HttpPost("{id:guid}/submit-review")]
    public async Task<IActionResult> SubmitForReview(Guid id, CancellationToken ct)
    {
        var post = await _workflow.GetPostAsync(id, ct);
        if (post is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));

        if (!_workflow.IsOwner(post) && !_workflow.IsInAnyRole("Admin"))
            return StatusCode(403, ApiResponse.Fail("FORBIDDEN", "Bạn không có quyền thực hiện thao tác này"));

        var result = await _workflow.SubmitForReviewAsync(id, ct);
        return Ok(ApiResponse.Ok(ToResponse(result), "Gửi duyệt thành công"));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Admin,Reviewer")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var result = await _workflow.ApproveAsync(id, ct);
        return Ok(ApiResponse.Ok(ToResponse(result), "Duyệt bài thành công"));
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin,Reviewer")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] RejectPostRequest request, CancellationToken ct)
    {
        var result = await _workflow.RejectAsync(id, request.Reason, ct);
        return Ok(ApiResponse.Ok(ToResponse(result), "Từ chối bài thành công"));
    }

    [HttpPost("{id:guid}/schedule")]
    public async Task<IActionResult> Schedule(Guid id, [FromBody] SchedulePostRequest request, CancellationToken ct)
    {
        var post = await _workflow.GetPostAsync(id, ct);
        if (post is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));

        if (!_workflow.IsOwner(post) && !_workflow.IsInAnyRole("Admin", "ContentManager"))
            return StatusCode(403, ApiResponse.Fail("FORBIDDEN", "Bạn không có quyền thực hiện thao tác này"));

        var result = await _workflow.ScheduleAsync(id, request.ScheduledAt, request.Timezone, ct);
        return Ok(ApiResponse.Ok(ToResponse(result), "Lên lịch đăng thành công"));
    }

    [HttpPost("{id:guid}/cancel-schedule")]
    public async Task<IActionResult> CancelSchedule(Guid id, CancellationToken ct)
    {
        var post = await _workflow.GetPostAsync(id, ct);
        if (post is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));

        if (!_workflow.IsOwner(post) && !_workflow.IsInAnyRole("Admin", "ContentManager"))
            return StatusCode(403, ApiResponse.Fail("FORBIDDEN", "Bạn không có quyền thực hiện thao tác này"));

        var result = await _workflow.CancelScheduleAsync(id, ct);
        return Ok(ApiResponse.Ok(ToResponse(result), "Hủy lịch đăng thành công"));
    }

    [HttpPost("{id:guid}/publish-now")]
    [Authorize(Roles = "Admin,Reviewer,ContentManager")]
    public async Task<IActionResult> PublishNow(Guid id, CancellationToken ct)
    {
        // publish-now = đăng NGAY: chuyển Publishing + tạo log Pending, rồi xử lý luôn (mock/real).
        await _workflow.PublishNowAsync(id, ct);

        try
        {
            var result = await _publishPipeline.ProcessPendingForPostAsync(id, ct);
            var post = await _workflow.GetPostAsync(id, ct);
            return Ok(ApiResponse.Ok(ToResponse(post!),
                result is not null ? "Đã đăng bài thành công" : "Đã tạo job đăng bài"));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Lỗi precondition/attempt → gỡ post khỏi Publishing để không bị kẹt.
            await _publishPipeline.RevertStuckPublishingAsync(id, ct);
            return BadRequest(ApiResponse.Fail("PUBLISH_FAILED", ex.Message));
        }
    }

    [HttpGet("{id:guid}/timeline")]
    public async Task<IActionResult> GetTimeline(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await _workflow.GetTimelineAsync(id, ct);
            return Ok(ApiResponse.Ok(result));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));
        }
    }
}
