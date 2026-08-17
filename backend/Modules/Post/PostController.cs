using Backend.Modules.PageContext;
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
    private readonly PostRecycleService _recycleService;
    private readonly GenerationJob.GenerationJobPipelineService _generationPipeline;
    private readonly PublishLog.IPublishPipelineService _publishPipeline;
    private readonly PageContextRepository _pageContextRepository;
    private readonly MediaAsset.PostMediaRepository _postMediaRepository;

    public PostController(
        PostRepository repository,
        PostWorkflowService workflow,
        PostRecycleService recycleService,
        GenerationJob.GenerationJobPipelineService generationPipeline,
        PublishLog.IPublishPipelineService publishPipeline,
        PageContextRepository pageContextRepository,
        MediaAsset.PostMediaRepository postMediaRepository) : base(repository)
    {
        _repo = repository;
        _workflow = workflow;
        _recycleService = recycleService;
        _generationPipeline = generationPipeline;
        _publishPipeline = publishPipeline;
        _pageContextRepository = pageContextRepository;
        _postMediaRepository = postMediaRepository;
    }

    protected override string EntityLabel => "bài viết";
    protected override PostResponse ToResponse(PostModel e) => PostRepository.ToResponse(e);

    public override async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var response = await _repo.GetResponseByIdAsync(id, ct);
        if (response is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", $"Không tìm thấy {EntityLabel}"));
        return Ok(ApiResponse.Ok(response));
    }

    protected override async Task<PostModel> CreateEntityAsync(CreatePostRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("Tiêu đề không được để trống");
        return await _repo.CreateAsync(request, ct);
    }

    protected override Task<PostModel?> UpdateEntityAsync(Guid id, UpdatePostRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<PostResponse>> FilterEntitiesAsync(PostFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    /// <summary>
    /// Xoá bài. Chặn khi bài đang có lịch đăng: lịch có thể đã đẩy sang Facebook, xoá bài trong
    /// hệ thống sẽ để lại "bài mồ côi" — Facebook vẫn đăng đúng giờ mà mình không còn đường huỷ.
    /// Người dùng phải huỷ lịch trước.
    /// </summary>
    public override async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        var post = await _workflow.GetPostAsync(id, ct);
        if (post is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));

        if (post.Status == PostStatus.Scheduled)
            return BadRequest(ApiResponse.Fail(
                "POST_SCHEDULED",
                "Bài đang có lịch đăng. Hãy huỷ lịch trước khi xoá."));

        return await base.SoftDelete(id, ct);
    }

    /// <summary>Soft-delete tất cả bài viết (Admin: toàn bộ; ContentManager: bài của mình).</summary>
    [HttpDelete("all")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> SoftDeleteAll(CancellationToken ct)
    {
        var deleteAllUsers = _workflow.IsInAnyRole("Admin");
        var deleted = await _repo.SoftDeleteAllAsync(deleteAllUsers, ct);
        return Ok(ApiResponse.Ok(new { deleted },
            deleted == 0
                ? "Không có bài viết nào để xóa (bài đang có lịch đăng được giữ lại)"
                : $"Đã xóa {deleted} bài viết — bài đang có lịch đăng được giữ lại"));
    }

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

        var channelIds = ResolveChannelIds(request);
        if (channelIds.Count == 0)
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Phải chọn ít nhất một kênh đăng"));

        var packId = request.PromptTemplateId is Guid p && p != Guid.Empty ? p : (Guid?)null;
        var pageMap = await _pageContextRepository.GetMapByChannelsAsync(channelIds, ct);
        var missing = channelIds
            .Where(id =>
            {
                if (packId.HasValue) return false;
                pageMap.TryGetValue(id, out var pc);
                return !PageContextRepository.HasTemplateReady(pc);
            })
            .ToList();

        if (missing.Count > 0)
            return BadRequest(ApiResponse.Fail(
                "VALIDATION_ERROR",
                "Có kênh chưa cấu hình PageContext (danh mục mặc định). Hãy chọn danh mục hoặc setup Page Context trước."));

        // Nhiều kênh → queue nền (tránh timeout), trả batchId.
        if (channelIds.Count > 1)
        {
            var bulk = await _repo.CreateFanOutQueuedAsync(
                title: request.Title.Trim(),
                channelIds: channelIds,
                generationFlow: request.GenerationFlow,
                promptTemplateId: packId,
                pageContextByChannel: pageMap,
                objective: request.Objective,
                categoryId: request.CategoryId,
                imageCount: request.ImageCount,
                generateAsReels: request.GenerateAsReels,
                ct: ct);

            return Ok(ApiResponse.Ok(bulk,
                $"Đã tạo {bulk.Created} bài — đang sinh nội dung nền, xem tiến độ ở batch."));
        }

        // 1 kênh: tạo + sinh đồng bộ.
        var channelId = channelIds[0];
        pageMap.TryGetValue(channelId, out var singlePc);
        var (pcText, pcImage) = PageContextRepository.ResolveDefaultTemplateIds(singlePc);
        var createReq = new CreatePostRequest
        {
            Title = request.Title,
            SocialChannelId = channelId,
            PromptTemplateId = packId,
            TextTemplateId = packId is null ? pcText : null,
            ImageTemplateId = packId is null ? pcImage : null,
            CategoryId = request.CategoryId,
            GenerationFlow = request.GenerationFlow,
            Objective = request.Objective,
            ImageCount = request.ImageCount
        };

        var post = await _repo.CreateAsync(createReq, ct);

        try
        {
            await GenerateTextThenImageAsync(post.Id, ct);
            if (request.GenerateAsReels)
                await _generationPipeline.ConvertToReelsAsync(post.Id, null, ct);
            await _workflow.ApproveAsync(post.Id, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            var partial = await _workflow.GetPostAsync(post.Id, ct);
            return Ok(ApiResponse.Ok(ToResponse(partial!),
                $"Đã tạo bài nhưng sinh nội dung chưa trọn vẹn: {ex.Message}. Có thể tạo lại ở màn preview."));
        }

        var final = await _workflow.GetPostAsync(post.Id, ct);
        var response = await _repo.GetResponseByIdAsync(final!.Id, ct);
        return Ok(ApiResponse.Ok(response!, "Đã tạo bài và sinh nội dung xong"));
    }

    /// <summary>
    /// Tạo batch bài mới từ các bài đã đăng (Published) của một kênh.
    /// Ảnh cũ được tái sử dụng, không sinh ảnh mới.
    /// flow=Recycle → copy nguyên văn, Status=Approved ngay.
    /// flow=RecycleRewrite → AI paraphrase, Status=Queued (worker xử lý).
    /// </summary>
    [HttpPost("recycle")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> RecyclePosts(
        [FromBody] RecyclePostRequest request,
        CancellationToken ct)
    {
        var result = await _recycleService.CreateRecycleBatchAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Tạo hàng loạt bài (items × channels). Trả về ngay; worker sinh nội dung nền.</summary>
    [HttpPost("bulk-create")]
    public async Task<IActionResult> BulkCreate([FromBody] BulkCreatePostRequest request, CancellationToken ct)
    {
        var hasPack = request.PromptTemplateId is Guid p && p != Guid.Empty;
        var hasLegacy = request.TextTemplateId.HasValue || request.ImageTemplateId.HasValue;

        // Không chọn danh mục cho batch → mỗi page dùng PageContext của nó (giống tạo bài đơn).
        // Chỉ bắt buộc chọn danh mục nếu có page CHƯA setup PageContext.
        var channelIds = (request.ChannelIds ?? []).Where(c => c != Guid.Empty).Distinct().ToList();
        var pageMap = await _pageContextRepository.GetMapByChannelsAsync(channelIds, ct);
        if (!hasPack && !hasLegacy)
        {
            var missing = channelIds
                .Where(id => { pageMap.TryGetValue(id, out var pc); return !PageContextRepository.HasTemplateReady(pc); })
                .ToList();
            if (missing.Count > 0)
                return BadRequest(ApiResponse.Fail(
                    "VALIDATION_ERROR",
                    "Có page chưa cấu hình PageContext — hãy chọn danh mục hoặc setup Page Context cho các page đó."));
        }

        try
        {
            var result = await _repo.BulkCreateAsync(request, pageMap, ct);
            return Ok(ApiResponse.Ok(result,
                $"Đã tạo {result.Created} bài — đang sinh nội dung nền, xem tiến độ ở batch."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Import CSV: 1 row = 1 bài / 1 kênh. Lịch pending trong ExtraJson → worker tự schedule sau Approved.
    /// </summary>
    [HttpPost("bulk-import")]
    public async Task<IActionResult> BulkImport([FromBody] BulkImportRequest request, CancellationToken ct)
    {
        var rows = (request.Rows ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r.Idea) && r.SocialChannelId != Guid.Empty)
            .ToList();
        if (rows.Count == 0)
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "File không có dòng hợp lệ (cần idea + kênh)."));

        var hasPack = request.PromptTemplateId is Guid p && p != Guid.Empty;
        var channelIds = rows.Select(r => r.SocialChannelId).Distinct().ToList();
        var pageMap = await _pageContextRepository.GetMapByChannelsAsync(channelIds, ct);
        if (!hasPack)
        {
            var missing = channelIds
                .Where(id => { pageMap.TryGetValue(id, out var pc); return !PageContextRepository.HasTemplateReady(pc); })
                .ToList();
            if (missing.Count > 0)
                return BadRequest(ApiResponse.Fail(
                    "VALIDATION_ERROR",
                    "Có page chưa cấu hình PageContext — hãy chọn danh mục ghi đè hoặc setup Page Context."));
        }

        try
        {
            request.Rows = rows;
            var result = await _repo.BulkImportAsync(request, pageMap, ct);
            return Ok(ApiResponse.Ok(result,
                $"Đã import {result.Created} bài — AI sinh nền, xong sẽ tự lên lịch nếu có scheduled_at."));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", ex.Message));
        }
    }

    private static List<Guid> ResolveChannelIds(CreatePostRequest request)
    {
        var fromList = (request.SocialChannelIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        if (fromList.Count > 0) return fromList;
        if (request.SocialChannelId != Guid.Empty) return [request.SocialChannelId];
        return [];
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

    /// <summary>
    /// Ghi đè khung hình Reels đang chọn (chỉnh tay qua ReelsFramePicker) — KHÔNG tự ghép video,
    /// chỉ lưu lựa chọn để convert-to-reels dùng sau. Áp dụng cho MỌI post đã có ảnh (FullAI hay
    /// Template), không phân biệt flow lúc tạo — Reels giờ là lựa chọn ở bước ĐĂNG.
    /// </summary>
    [HttpPut("{id:guid}/reels-frames")]
    public async Task<IActionResult> SetReelsFrames(
        Guid id, [FromBody] SetReelsFramesRequest request, CancellationToken ct)
    {
        var guard = await EnsureGenerationPermissionAsync(id, ct);
        if (guard is not null) return guard;

        var mediaIds = (request.MediaIds ?? []).Where(x => x != Guid.Empty).Distinct().ToList();
        if (mediaIds.Count == 0)
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Cần chọn ít nhất 1 khung hình"));

        await _postMediaRepository.ReplaceTemplateSourcesAsync(id, mediaIds, ct);
        return Ok(ApiResponse.Ok(true, $"Đã lưu {mediaIds.Count} khung hình — bấm Đăng dạng Reels để áp dụng"));
    }

    /// <summary>
    /// Biến ảnh bài đang có thành video rồi đăng Reels — áp dụng cho MỌI post đã có ảnh (FullAI 1
    /// ảnh AI hay Template N ảnh). Không truyền mediaIds thì dùng đúng ảnh bài đang có (mặc định,
    /// không cần chỉnh tay); truyền mediaIds thì dùng đúng thứ tự đó (mix ảnh đã render sẵn của
    /// post lẫn ảnh mới chọn từ MediaFolder — ảnh mới sẽ tự render chữ cho nhất quán).
    /// </summary>
    [HttpPost("{id:guid}/convert-to-reels")]
    public async Task<IActionResult> ConvertToReels(
        Guid id, [FromBody] ConvertToReelsRequest? request, CancellationToken ct)
    {
        var guard = await EnsureGenerationPermissionAsync(id, ct);
        if (guard is not null) return guard;

        await _generationPipeline.ConvertToReelsAsync(id, request?.MediaIds, ct, request?.MusicTrackId);
        await _workflow.ApproveAsync(id, ct);

        var post = await _workflow.GetPostAsync(id, ct);
        return Ok(ApiResponse.Ok(ToResponse(post!), "Đã đăng dạng Reels"));
    }

    private Task GenerateTextThenImageAsync(Guid postId, CancellationToken ct)
        => _generationPipeline.GenerateForPostAsync(postId, ct);

    // --- Bulk (tạo hàng loạt) ---

    /// <summary>Duyệt hàng loạt các bài WaitingReview trong batch (hoặc theo postIds).</summary>
    [HttpPost("bulk-approve")]
    [Authorize(Roles = "Admin,Reviewer")]
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
        var times = ScheduleSlotHelper.ComputeSlotTimesUtc(
            request.StartAtUtc ?? DateTime.UtcNow, request.TimeSlots, request.Timezone,
            posts.Count, request.JitterMinutes);

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
            Message = request.JitterMinutes > 0
                ? $"Đã lên lịch {ok.Count}/{posts.Count} bài, rải theo khung giờ (lệch ngẫu nhiên ±{request.JitterMinutes} phút)"
                : $"Đã lên lịch {ok.Count}/{posts.Count} bài, rải theo khung giờ"
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
            posts = posts.Select(p => PostRepository.ToResponse(p)).ToList()
        }));
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

        var result = await _workflow.ScheduleAsync(
            id, request.ScheduledAt, request.Timezone, ct, request.TikTokPostMode);
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
    public async Task<IActionResult> PublishNow(Guid id, [FromBody] PublishNowRequest? request, CancellationToken ct)
    {
        // publish-now = đăng NGAY: chuyển Publishing + tạo log Pending, rồi xử lý luôn (mock/real).
        await _workflow.PublishNowAsync(id, ct, request?.TikTokPostMode);

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
