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

    public PostController(
        PostRepository repository,
        PostWorkflowService workflow,
        GenerationJob.GenerationJobPipelineService generationPipeline) : base(repository)
    {
        _repo = repository;
        _workflow = workflow;
        _generationPipeline = generationPipeline;
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
        var result = await _workflow.PublishNowAsync(id, ct);
        return Ok(ApiResponse.Ok(ToResponse(result), "Đã tạo job đăng bài"));
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
