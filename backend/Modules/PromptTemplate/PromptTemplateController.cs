using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.PromptTemplate;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,ContentManager")]
public class PromptTemplateController
    : BaseController<PromptTemplateModel, PromptTemplateRepository,
        CreatePromptTemplateRequest, UpdatePromptTemplateRequest,
        PromptTemplateFilterRequest, PromptTemplateResponse>
{
    private readonly PromptTemplateRepository _repo;

    public PromptTemplateController(PromptTemplateRepository repository) : base(repository)
        => _repo = repository;

    protected override string EntityLabel => "template prompt";
    protected override PromptTemplateResponse ToResponse(PromptTemplateModel e)
        => PromptTemplateRepository.ToResponse(e);

    protected override Task<PromptTemplateModel> CreateEntityAsync(
        CreatePromptTemplateRequest request, CancellationToken ct)
        => _repo.CreateAsync(request, ct);

    protected override Task<PromptTemplateModel?> UpdateEntityAsync(
        Guid id, UpdatePromptTemplateRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<PromptTemplateResponse>> FilterEntitiesAsync(
        PromptTemplateFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    /// <summary>Danh sách biến động dùng được trong Body template (cho UI gợi ý).</summary>
    [HttpGet("variables")]
    [AllowAnonymous]
    public IActionResult GetVariables()
        => Ok(ApiResponse.Ok(PromptTemplateRenderer.AvailableVariables));

    /// <summary>Import hàng loạt danh mục template (JSON body).</summary>
    [HttpPost("bulk-import")]
    public async Task<IActionResult> BulkImport(
        [FromBody] BulkImportPromptTemplatesRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _repo.BulkImportAsync(request, ct);
            return Ok(ApiResponse.Ok(result, result.Message ?? "Import xong"));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", ex.Message));
        }
    }
}
