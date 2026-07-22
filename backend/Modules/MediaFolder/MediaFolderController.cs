using Backend.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.MediaFolder;

[ApiController]
[Route("api/[controller]")]
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

    protected override async Task<MediaFolderModel> CreateEntityAsync(CreateMediaFolderRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Tên thư mục không được để trống");
        return await _repo.CreateAsync(request, ct);
    }

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
