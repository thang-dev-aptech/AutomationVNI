using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.ChannelGroup;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChannelGroupController
    : BaseController<ChannelGroupModel, ChannelGroupRepository,
        CreateChannelGroupRequest, UpdateChannelGroupRequest,
        ChannelGroupFilterRequest, ChannelGroupResponse>
{
    private readonly ChannelGroupRepository _repo;

    public ChannelGroupController(ChannelGroupRepository repository) : base(repository)
        => _repo = repository;

    protected override string EntityLabel => "nhóm kênh";
    protected override ChannelGroupResponse ToResponse(ChannelGroupModel e)
        => throw new NotSupportedException("Dùng GetResponseByIdAsync / GetAllResponsesAsync.");

    protected override Task<ChannelGroupModel> CreateEntityAsync(
        CreateChannelGroupRequest request, CancellationToken ct)
        => _repo.CreateAsync(request, ct);

    protected override Task<ChannelGroupModel?> UpdateEntityAsync(
        Guid id, UpdateChannelGroupRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<ChannelGroupResponse>> FilterEntitiesAsync(
        ChannelGroupFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    /// <summary>Mọi vai trò đã đăng nhập xem được danh sách nhóm + kênh (VNi-first).</summary>
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
        [FromBody] CreateChannelGroupRequest request, CancellationToken ct)
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
        Guid id, [FromBody] UpdateChannelGroupRequest request, CancellationToken ct)
    {
        var entity = await UpdateEntityAsync(id, request, ct);
        if (entity is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", $"Không tìm thấy {EntityLabel}"));
        var response = await _repo.GetResponseByIdAsync(entity.Id, ct);
        return Ok(ApiResponse.Ok(response!, $"Cập nhật {EntityLabel} thành công"));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin,ContentManager")]
    public override Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
        => base.SoftDelete(id, ct);
}
