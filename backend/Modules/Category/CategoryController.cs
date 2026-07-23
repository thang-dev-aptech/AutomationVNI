using Backend.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Category;

[ApiController]
[Route("api/[controller]")]
public class CategoryController
    : BaseController<CategoryModel, CategoryRepository,
        CreateCategoryRequest, UpdateCategoryRequest,
        CategoryFilterRequest, CategoryResponse>
{
    private readonly CategoryRepository _repo;

    public CategoryController(CategoryRepository repository) : base(repository)
        => _repo = repository;

    protected override string EntityLabel => "danh mục";
    protected override CategoryResponse ToResponse(CategoryModel e) => CategoryRepository.ToResponse(e);

    protected override async Task<CategoryModel> CreateEntityAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Tên danh mục không được để trống");
        if (string.IsNullOrWhiteSpace(request.Slug)) throw new ArgumentException("Slug không được để trống");
        return await _repo.CreateAsync(request, ct);
    }

    protected override Task<CategoryModel?> UpdateEntityAsync(Guid id, UpdateCategoryRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<CategoryResponse>> FilterEntitiesAsync(CategoryFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    /// <summary>Import nhanh nhiều loại bài từ danh sách tên (mỗi tên 1 dòng).</summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] CategoryImportRequest request, CancellationToken ct)
    {
        if (request?.Names is null || request.Names.Count == 0)
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Chưa có tên loại bài nào để import"));

        var result = await _repo.ImportAsync(request.Names, request.ParentCategoryId, ct);
        return Ok(ApiResponse.Ok(result, $"Đã thêm {result.Created} loại bài; bỏ qua {result.Skipped}"));
    }
}
