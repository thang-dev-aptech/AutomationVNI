using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.Modules.PublishLog;

[ApiController]
[Route("api/[controller]")]
public class PublishLogController
    : BaseController<PublishLogModel, PublishLogRepository,
        CreatePublishLogRequest, UpdatePublishLogRequest,
        PublishLogFilterRequest, PublishLogResponse>
{
    private readonly PublishLogRepository _repo;
    private readonly IPublishPipelineService _pipeline;
    private readonly SchedulerOptions _schedulerOptions;

    public PublishLogController(
        PublishLogRepository repository,
        IPublishPipelineService pipeline,
        Microsoft.Extensions.Options.IOptions<SchedulerOptions> schedulerOptions) : base(repository)
    {
        _repo = repository;
        _pipeline = pipeline;
        _schedulerOptions = schedulerOptions.Value;
    }

    protected override string EntityLabel => "publish log";
    protected override PublishLogResponse ToResponse(PublishLogModel e) => PublishLogRepository.ToResponse(e);

    protected override Task<PublishLogModel> CreateEntityAsync(CreatePublishLogRequest request, CancellationToken ct)
        => _repo.CreateAsync(request, ct);

    protected override Task<PublishLogModel?> UpdateEntityAsync(Guid id, UpdatePublishLogRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<PublishLogResponse>> FilterEntitiesAsync(PublishLogFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    [HttpGet("by-post/{postId:guid}")]
    public async Task<IActionResult> GetByPost(Guid postId, CancellationToken ct)
    {
        var items = await _repo.GetByPostAsync(postId, ct);
        return Ok(ApiResponse.Ok(items.Select(PublishLogRepository.ToResponse).ToList()));
    }

    // --- Mock publish pipeline ---

    [HttpPost("{id:guid}/process")]
    [Authorize(Roles = "Admin,ContentManager,Reviewer")]
    public async Task<IActionResult> Process(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await _pipeline.ProcessAsync(id, ct);
            return Ok(ApiResponse.Ok(result, "Publish thành công"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy publish log"));
        }
    }

    [HttpPost("{id:guid}/process-real")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> ProcessReal(Guid id, CancellationToken ct)
    {
        try
        {
            var result = await _pipeline.ProcessRealAsync(id, ct);
            return Ok(ApiResponse.Ok(result, "Publish (real Facebook) thành công"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy publish log"));
        }
    }

    [HttpPost("{id:guid}/fail")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Fail(Guid id, [FromBody] FailPublishLogRequest request, CancellationToken ct)
    {
        try
        {
            var log = await _pipeline.FailAsync(id, request, ct);
            return Ok(ApiResponse.Ok(PublishLogRepository.ToResponse(log), "Đã ghi nhận lỗi publish"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy publish log"));
        }
    }

    [HttpPost("{id:guid}/retry")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        try
        {
            var log = await _pipeline.RetryAsync(id, ct);
            return Ok(ApiResponse.Ok(PublishLogRepository.ToResponse(log), "Retry publish thành công"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy publish log"));
        }
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        try
        {
            var log = await _pipeline.CancelAsync(id, ct);
            return Ok(ApiResponse.Ok(PublishLogRepository.ToResponse(log), "Hủy publish thành công"));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy publish log"));
        }
    }

    [HttpPost("process-due")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> ProcessDue([FromQuery] int? batchSize, CancellationToken ct)
    {
        var size = batchSize ?? _schedulerOptions.BatchSize;
        var result = await _pipeline.ProcessDueScheduledAsync(size, ct);
        return Ok(ApiResponse.Ok(result, $"Đã xử lý {result.Picked} bài scheduled"));
    }
}
