using Backend.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.PageContext;

[ApiController]
[Route("api/[controller]")]
public class PageContextController
    : BaseController<PageContextModel, PageContextRepository,
        CreatePageContextRequest, UpdatePageContextRequest,
        PageContextFilterRequest, PageContextResponse>
{
    private readonly PageContextRepository _repo;

    public PageContextController(PageContextRepository repository) : base(repository)
        => _repo = repository;

    protected override string EntityLabel => "page context";
    protected override PageContextResponse ToResponse(PageContextModel e) => PageContextRepository.ToResponse(e);

    protected override async Task<PageContextModel> CreateEntityAsync(CreatePageContextRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BrandName)) throw new ArgumentException("Tên thương hiệu không được để trống");
        return await _repo.CreateAsync(request, ct);
    }

    protected override Task<PageContextModel?> UpdateEntityAsync(Guid id, UpdatePageContextRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<PageContextResponse>> FilterEntitiesAsync(PageContextFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    [HttpGet("by-channel/{channelId:guid}")]
    public async Task<IActionResult> GetByChannel(Guid channelId, CancellationToken ct)
    {
        var entity = await _repo.GetByChannelAsync(channelId, ct);
        if (entity is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Chưa có page context cho kênh này"));
        return Ok(ApiResponse.Ok(PageContextRepository.ToResponse(entity)));
    }
}
