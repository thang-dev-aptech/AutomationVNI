using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.Campaign;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CampaignController
    : BaseController<CampaignModel, CampaignRepository,
        CreateCampaignRequest, UpdateCampaignRequest,
        CampaignFilterRequest, CampaignResponse>
{
    private readonly CampaignRepository _repo;

    public CampaignController(CampaignRepository repository) : base(repository)
        => _repo = repository;

    protected override string EntityLabel => "chiến dịch";

    protected override CampaignResponse ToResponse(CampaignModel e)
        => throw new NotSupportedException("Dùng GetResponseByIdAsync / GetAllResponsesAsync.");

    protected override Task<CampaignModel> CreateEntityAsync(
        CreateCampaignRequest request, CancellationToken ct)
        => _repo.CreateAsync(request, ct);

    protected override Task<CampaignModel?> UpdateEntityAsync(
        Guid id, UpdateCampaignRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<CampaignResponse>> FilterEntitiesAsync(
        CampaignFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    [HttpGet]
    public override async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await _repo.GetAllResponsesAsync(ct);
        return Ok(ApiResponse.Ok(items));
    }

    [HttpGet("{id:guid}")]
    public override async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var response = await _repo.GetResponseByIdAsync(id, ct);
        if (response is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", $"Không tìm thấy {EntityLabel}"));
        return Ok(ApiResponse.Ok(response));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,ContentManager")]
    public override async Task<IActionResult> Create(
        [FromBody] CreateCampaignRequest request, CancellationToken ct)
    {
        var entity = await CreateEntityAsync(request, ct);
        var response = await _repo.GetResponseByIdAsync(entity.Id, ct);
        return CreatedAtAction(
            nameof(GetById),
            new { id = entity.Id },
            ApiResponse.Ok(response!, $"Tạo {EntityLabel} thành công"));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public override async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateCampaignRequest request, CancellationToken ct)
    {
        var entity = await UpdateEntityAsync(id, request, ct);
        if (entity is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", $"Không tìm thấy {EntityLabel}"));
        var response = await _repo.GetResponseByIdAsync(entity.Id, ct);
        return Ok(ApiResponse.Ok(response!, $"Cập nhật {EntityLabel} thành công"));
    }

    [HttpPost("{id:guid}/pause")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Pause(Guid id, CancellationToken ct)
    {
        var entity = await _repo.PauseAsync(id, ct);
        if (entity is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", $"Không tìm thấy {EntityLabel}"));
        var response = await _repo.GetResponseByIdAsync(entity.Id, ct);
        return Ok(ApiResponse.Ok(response!, "Đã tạm dừng chiến dịch"));
    }

    [HttpPost("{id:guid}/resume")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        var entity = await _repo.ResumeAsync(id, ct);
        if (entity is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", $"Không tìm thấy {EntityLabel}"));
        var response = await _repo.GetResponseByIdAsync(entity.Id, ct);
        return Ok(ApiResponse.Ok(response!, "Đã tiếp tục chiến dịch"));
    }

    [HttpPost("{id:guid}/end")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> End(Guid id, CancellationToken ct)
    {
        var entity = await _repo.EndAsync(id, ct);
        if (entity is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", $"Không tìm thấy {EntityLabel}"));
        var response = await _repo.GetResponseByIdAsync(entity.Id, ct);
        return Ok(ApiResponse.Ok(response!, "Đã kết thúc chiến dịch"));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public override Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
        => base.SoftDelete(id, ct);
}
