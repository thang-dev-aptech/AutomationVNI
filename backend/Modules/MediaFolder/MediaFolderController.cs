using Backend.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.MediaFolder;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,ContentManager,Reviewer,Viewer")]
public class MediaFolderController
    : BaseController<MediaFolderModel, MediaFolderRepository,
        CreateMediaFolderRequest, UpdateMediaFolderRequest,
        MediaFolderFilterRequest, MediaFolderResponse>
{
    private readonly MediaFolderRepository _repo;

    public MediaFolderController(MediaFolderRepository repository) : base(repository)
        => _repo = repository;

    protected override string EntityLabel => "thư mục";
    protected override MediaFolderResponse ToResponse(MediaFolderModel e) => MediaFolderRepository.ToResponse(e);

    /// <summary>
    /// Duyệt folder một cấp theo Page (SocialChannelId) và ParentFolderId.
    /// ParentFolderId = null: lấy thư mục gốc của Page.
    /// ParentFolderId != null: lấy thư mục con trực tiếp 1 cấp.
    /// Trả ChildFolderCount và DirectAssetCount; không tải descendants.
    /// </summary>
    [HttpGet("children")]
    public async Task<IActionResult> GetChildren(
        [FromQuery] GetMediaFolderChildrenRequest request,
        CancellationToken ct)
    {
        var result = await _repo.GetChildrenAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>
    /// Hỗ trợ duyệt folder một cấp bằng request body POST.
    /// </summary>
    [HttpPost("children")]
    public async Task<IActionResult> PostChildren(
        [FromBody] GetMediaFolderChildrenRequest request,
        CancellationToken ct)
    {
        var result = await _repo.GetChildrenAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }

    [Authorize(Roles = "Admin,ContentManager")]
    protected override async Task<MediaFolderModel> CreateEntityAsync(CreateMediaFolderRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Tên thư mục không được để trống");
        return await _repo.CreateAsync(request, ct);
    }

    [Authorize(Roles = "Admin,ContentManager")]
    protected override Task<MediaFolderModel?> UpdateEntityAsync(Guid id, UpdateMediaFolderRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<MediaFolderResponse>> FilterEntitiesAsync(MediaFolderFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    /// <summary>Toàn bộ cây folder cho sidebar (kèm số ảnh + cờ có thư mục con).</summary>
    [HttpGet("tree")]
    public async Task<IActionResult> GetTree(CancellationToken ct)
    {
        var tree = await _repo.GetTreeAsync(ct);
        return Ok(ApiResponse.Ok(tree));
    }
}
