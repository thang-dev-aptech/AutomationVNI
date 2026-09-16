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

    /// <summary>
    /// Breadcrumb từ gốc Page đến folder đích (MEDIA-02). Không đi xuyên Page; folder sai scope trả 404.
    /// </summary>
    [HttpGet("breadcrumb")]
    public async Task<IActionResult> GetBreadcrumb(
        [FromQuery] GetMediaFolderBreadcrumbRequest request,
        CancellationToken ct)
    {
        var result = await _repo.GetBreadcrumbAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }

    [HttpPost("breadcrumb")]
    public async Task<IActionResult> PostBreadcrumb(
        [FromBody] GetMediaFolderBreadcrumbRequest request,
        CancellationToken ct)
    {
        var result = await _repo.GetBreadcrumbAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>
    /// Tìm folder toàn Page theo tên (có/không dấu), kèm full path và counts (MEDIA-02).
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchFolders(
        [FromQuery] SearchMediaFoldersRequest request,
        CancellationToken ct)
    {
        var result = await _repo.SearchFoldersAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }

    [HttpPost("search")]
    public async Task<IActionResult> PostSearchFolders(
        [FromBody] SearchMediaFoldersRequest request,
        CancellationToken ct)
    {
        var result = await _repo.SearchFoldersAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>
    /// Tạo thư mục hàng loạt trong một Page theo cấu trúc cây (clientRef/parentRef) (MEDIA-03).
    /// Hỗ trợ validate-only/preview, xử lý duplicate và đảm bảo transaction nguyên tử.
    /// </summary>
    [HttpPost("bulk")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> BulkCreate(
        [FromBody] BulkCreateMediaFolderRequest request,
        CancellationToken ct)
    {
        var result = await _repo.BulkCreateAsync(request, ct);
        return Ok(ApiResponse.Ok(result, result.ValidateOnly ? "Kiểm tra hợp lệ cấu trúc thư mục thành công" : "Tạo thư mục hàng loạt thành công"));
    }

    protected override async Task<MediaFolderModel> CreateEntityAsync(CreateMediaFolderRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Tên thư mục không được để trống");
        return await _repo.CreateAsync(request, ct);
    }

    protected override Task<MediaFolderModel?> UpdateEntityAsync(Guid id, UpdateMediaFolderRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<MediaFolderResponse>> FilterEntitiesAsync(MediaFolderFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    // [Authorize] trên CreateEntityAsync/UpdateEntityAsync (protected, không phải action) không được
    // MVC pipeline áp dụng — phải override đúng action Create/Update/SoftDelete của BaseController.
    [Authorize(Roles = "Admin,ContentManager")]
    public override Task<IActionResult> Create([FromBody] CreateMediaFolderRequest request, CancellationToken ct)
        => base.Create(request, ct);

    [Authorize(Roles = "Admin,ContentManager")]
    public override Task<IActionResult> Update(Guid id, [FromBody] UpdateMediaFolderRequest request, CancellationToken ct)
        => base.Update(id, request, ct);

    [Authorize(Roles = "Admin,ContentManager")]
    public override Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
        => base.SoftDelete(id, ct);

    /// <summary>Toàn bộ cây folder cho sidebar (kèm số ảnh + cờ có thư mục con).</summary>
    [HttpGet("tree")]
    public async Task<IActionResult> GetTree(CancellationToken ct)
    {
        var tree = await _repo.GetTreeAsync(ct);
        return Ok(ApiResponse.Ok(tree));
    }
}
