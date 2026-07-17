using Backend.Shared;
using Backend.Shared.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.MediaAsset;

[ApiController]
[Route("api/[controller]")]
public class MediaAssetController
    : BaseController<MediaAssetModel, MediaAssetRepository,
        CreateMediaAssetRequest, UpdateMediaAssetRequest,
        MediaAssetFilterRequest, MediaAssetResponse>
{
    private readonly MediaAssetRepository _repo;
    private readonly IFileStorageService _fileStorage;

    public MediaAssetController(MediaAssetRepository repository, IFileStorageService fileStorage) : base(repository)
    {
        _repo = repository;
        _fileStorage = fileStorage;
    }

    protected override string EntityLabel => "media";
    protected override MediaAssetResponse ToResponse(MediaAssetModel e) => MediaAssetRepository.ToResponse(e);

    protected override async Task<MediaAssetModel> CreateEntityAsync(CreateMediaAssetRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FileName)) throw new ArgumentException("FileName không được để trống");
        return await _repo.CreateAsync(request, ct);
    }

    protected override Task<MediaAssetModel?> UpdateEntityAsync(Guid id, UpdateMediaAssetRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<MediaAssetResponse>> FilterEntitiesAsync(MediaAssetFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] Guid? categoryId,
        [FromForm] string? altText,
        CancellationToken ct)
    {
        var saveResult = await _fileStorage.SaveAsync(file, "uploads", ct);
        var entity = await _repo.CreateFromUploadAsync(saveResult, categoryId, altText, ct);
        return CreatedAtAction(
            nameof(GetById),
            new { id = entity.Id },
            ApiResponse.Ok(ToResponse(entity), "Upload media thành công"));
    }

    [HttpGet("{id:guid}/preview")]
    [AllowAnonymous]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
        => await StreamFileAsync(id, download: false, ct);

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
        => await StreamFileAsync(id, download: true, ct);

    private async Task<IActionResult> StreamFileAsync(Guid id, bool download, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy media"));

        if (string.IsNullOrWhiteSpace(entity.StoragePath)
            || !await _fileStorage.ExistsAsync(entity.StoragePath, ct))
            return NotFound(ApiResponse.Fail("FILE_NOT_FOUND", "File không tồn tại trên storage"));

        var stream = await _fileStorage.OpenReadAsync(entity.StoragePath, ct);
        var fileName = entity.OriginalFileName ?? entity.FileName;

        if (download)
            return File(stream, entity.MimeType, fileName);

        return File(stream, entity.MimeType);
    }
}

[ApiController]
[Route("api/[controller]")]
public class PostMediaController
    : BaseController<PostMediaModel, PostMediaRepository,
        CreatePostMediaRequest, UpdatePostMediaRequest,
        PostMediaFilterRequest, PostMediaResponse>
{
    private readonly PostMediaRepository _repo;

    public PostMediaController(PostMediaRepository repository) : base(repository)
        => _repo = repository;

    protected override string EntityLabel => "post media";
    protected override PostMediaResponse ToResponse(PostMediaModel e) => PostMediaRepository.ToResponse(e);

    protected override Task<PostMediaModel> CreateEntityAsync(CreatePostMediaRequest request, CancellationToken ct)
        => _repo.CreateAsync(request, ct);

    protected override Task<PostMediaModel?> UpdateEntityAsync(Guid id, UpdatePostMediaRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<PostMediaResponse>> FilterEntitiesAsync(PostMediaFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    [HttpGet("by-post/{postId:guid}")]
    public async Task<IActionResult> GetByPost(Guid postId, CancellationToken ct)
    {
        var items = await _repo.GetByPostAsync(postId, ct);
        return Ok(ApiResponse.Ok(items.Select(PostMediaRepository.ToResponse).ToList()));
    }
}
