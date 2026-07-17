using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.GenerationJob;

[ApiController]
[Route("api/[controller]")]
public class GenerationJobController
    : BaseController<GenerationJobModel, GenerationJobRepository,
        CreateGenerationJobRequest, UpdateGenerationJobRequest,
        GenerationJobFilterRequest, GenerationJobResponse>
{
    private readonly GenerationJobRepository _repo;
    private readonly GenerationJobPipelineService _pipeline;

    public GenerationJobController(
        GenerationJobRepository repository,
        GenerationJobPipelineService pipeline) : base(repository)
    {
        _repo = repository;
        _pipeline = pipeline;
    }

    protected override string EntityLabel => "generation job";
    protected override GenerationJobResponse ToResponse(GenerationJobModel e) => GenerationJobRepository.ToResponse(e);

    protected override Task<GenerationJobModel> CreateEntityAsync(CreateGenerationJobRequest request, CancellationToken ct)
        => _repo.CreateAsync(request, ct);

    protected override Task<GenerationJobModel?> UpdateEntityAsync(Guid id, UpdateGenerationJobRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<GenerationJobResponse>> FilterEntitiesAsync(GenerationJobFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    [HttpGet("by-post/{postId:guid}")]
    public async Task<IActionResult> GetByPost(Guid postId, CancellationToken ct)
    {
        var items = await _repo.GetByPostAsync(postId, ct);
        return Ok(ApiResponse.Ok(items.Select(GenerationJobRepository.ToResponse).ToList()));
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPending([FromQuery] int batchSize = 10, CancellationToken ct = default)
    {
        var items = await _repo.GetPendingJobsAsync(batchSize, ct);
        return Ok(ApiResponse.Ok(items.Select(GenerationJobRepository.ToResponse).ToList()));
    }

    // --- Mock pipeline actions ---

    [HttpPost("{id:guid}/process")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Process(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await _pipeline.ProcessAsync(id, ct);
            return Ok(ApiResponse.Ok(result, "Xử lý job thành công"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy generation job"));
        }
    }

    [HttpPost("{id:guid}/fail")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Fail(Guid id, [FromBody] FailGenerationJobRequest request, CancellationToken ct)
    {
        try
        {
            var job = await _pipeline.FailAsync(id, request, ct);
            return Ok(ApiResponse.Ok(GenerationJobRepository.ToResponse(job), "Đã ghi nhận lỗi job"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy generation job"));
        }
    }

    [HttpPost("{id:guid}/retry")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        try
        {
            var job = await _pipeline.RetryAsync(id, ct);
            return Ok(ApiResponse.Ok(GenerationJobRepository.ToResponse(job), "Retry job thành công"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy generation job"));
        }
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            var job = await _pipeline.CancelAsync(id, ct);
            return Ok(ApiResponse.Ok(GenerationJobRepository.ToResponse(job), "Hủy job thành công"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy generation job"));
        }
    }
}
