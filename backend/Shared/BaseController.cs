using Backend.Shared.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Shared;

/// <summary>
/// BaseController generic — cung cấp sẵn CRUD + Filter endpoints chuẩn.
/// TRepository phải kế thừa GenericRepository và implement các method chuẩn.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public abstract class BaseController<TEntity, TRepository, TCreateRequest, TUpdateRequest, TFilterRequest, TResponse>
    : ControllerBase
    where TEntity : BaseEntity
    where TRepository : GenericRepository<TEntity>
    where TFilterRequest : PagedFilterRequest
{
    protected readonly TRepository Repository;

    protected BaseController(TRepository repository) => Repository = repository;

    // Subclass implement để map entity → response DTO
    protected abstract TResponse ToResponse(TEntity entity);

    // Subclass implement Create/Update với DTO nghiệp vụ, trả về entity đã lưu
    protected abstract Task<TEntity> CreateEntityAsync(TCreateRequest request, CancellationToken ct);
    protected abstract Task<TEntity?> UpdateEntityAsync(Guid id, TUpdateRequest request, CancellationToken ct);

    // Subclass có thể override Filter nếu cần query đặc biệt
    protected virtual async Task<PagedResult<TResponse>> FilterEntitiesAsync(TFilterRequest request, CancellationToken ct)
    {
        var paged = await Repository.PaginatePublicAsync(request.Keyword, request.Index, request.Size, ct);
        return new PagedResult<TResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    // Human-readable entity name cho error message, subclass có thể override
    protected virtual string EntityLabel => "Dữ liệu";

    [HttpGet]
    public virtual async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await Repository.GetAllAsync(ct);
        return Ok(ApiResponse.Ok(items.Select(ToResponse).ToList()));
    }

    [HttpGet("{id:guid}")]
    public virtual async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var entity = await Repository.GetByIdAsync(id, ct);
        if (entity is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", $"Không tìm thấy {EntityLabel}"));
        return Ok(ApiResponse.Ok(ToResponse(entity)));
    }

    [HttpPost("filter")]
    public virtual async Task<IActionResult> Filter([FromBody] TFilterRequest request, CancellationToken ct)
    {
        var result = await FilterEntitiesAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }

    [HttpPost]
    public virtual async Task<IActionResult> Create([FromBody] TCreateRequest request, CancellationToken ct)
    {
        var entity = await CreateEntityAsync(request, ct);
        return CreatedAtAction(
            nameof(GetById),
            new { id = entity.Id },
            ApiResponse.Ok(ToResponse(entity), $"Tạo {EntityLabel} thành công"));
    }

    [HttpPut("{id:guid}")]
    public virtual async Task<IActionResult> Update(Guid id, [FromBody] TUpdateRequest request, CancellationToken ct)
    {
        var entity = await UpdateEntityAsync(id, request, ct);
        if (entity is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", $"Không tìm thấy {EntityLabel}"));
        return Ok(ApiResponse.Ok(ToResponse(entity), $"Cập nhật {EntityLabel} thành công"));
    }

    [HttpDelete("{id:guid}")]
    public virtual async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        var deleted = await Repository.SoftDeleteAsync(id, ct);
        if (!deleted)
            return NotFound(ApiResponse.Fail("NOT_FOUND", $"Không tìm thấy {EntityLabel}"));
        return Ok(ApiResponse.Ok($"Xóa {EntityLabel} thành công"));
    }
}
