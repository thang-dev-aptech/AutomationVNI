using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.ApiLog;

// ApiLog không expose Create/Update — chỉ đọc + filter
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ApiLogController(ApiLogRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await repository.GetAllAsync(ct);
        return Ok(ApiResponse.Ok(items.Select(ApiLogRepository.ToResponse).ToList()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var entity = await repository.GetByIdAsync(id, ct);
        if (entity is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy log"));
        return Ok(ApiResponse.Ok(ApiLogRepository.ToDetailResponse(entity)));
    }

    [HttpPost("filter")]
    public async Task<IActionResult> Filter([FromBody] ApiLogFilterRequest request, CancellationToken ct)
    {
        var result = await repository.FilterAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }
}
