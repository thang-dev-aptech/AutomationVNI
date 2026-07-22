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
    private readonly MediaIntelligenceService _intelligence;

    public MediaAssetController(
        MediaAssetRepository repository,
        IFileStorageService fileStorage,
        MediaIntelligenceService intelligence) : base(repository)
    {
        _repo = repository;
        _fileStorage = fileStorage;
        _intelligence = intelligence;
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
        [FromForm] Guid? folderId,
        [FromForm] List<Guid>? categoryIds,
        CancellationToken ct)
    {
        var saveResult = await _fileStorage.SaveAsync(file, "uploads", ct);
        var entity = await _repo.CreateFromUploadAsync(saveResult, categoryId, altText, folderId, categoryIds, ct);
        if (entity.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                entity = await _intelligence.AnalyzeAndSaveAsync(entity.Id, ct);
            }
            catch
            {
                // Upload vẫn thành công khi AI tạm lỗi; người dùng có thể bấm Phân tích lại.
            }
        }
        return CreatedAtAction(
            nameof(GetById),
            new { id = entity.Id },
            ApiResponse.Ok(ToResponse(entity), "Upload media thành công"));
    }

    /// <summary>
    /// Upload nhiều ảnh 1 lần (cả folder). Mỗi ảnh lưu + AI gắn nhãn tuần tự (tránh dồn request GPT).
    /// Ảnh lỗi (quá lớn / không phải ảnh / AI lỗi) không làm hỏng cả batch.
    /// </summary>
    [HttpPost("upload-batch")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadBatch(
        [FromForm] List<IFormFile> files,
        [FromForm] Guid? folderId,
        [FromForm] List<Guid>? categoryIds,
        CancellationToken ct)
    {
        if (files is null || files.Count == 0)
            return BadRequest(ApiResponse.Fail("VALIDATION_ERROR", "Chưa chọn file nào để upload"));

        var items = new List<MediaAssetResponse>();
        var errors = new List<string>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var saveResult = await _fileStorage.SaveAsync(file, "uploads", ct);
                var entity = await _repo.CreateFromUploadAsync(saveResult, null, null, folderId, categoryIds, ct);
                if (entity.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    try { entity = await _intelligence.AnalyzeAndSaveAsync(entity.Id, ct); }
                    catch { /* Ảnh vẫn lưu; có thể "Phân tích lại" sau. */ }
                }
                items.Add(ToResponse(entity));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        return Ok(ApiResponse.Ok(
            new { uploaded = items.Count, failed = errors.Count, items, errors },
            $"Đã upload {items.Count} ảnh; lỗi {errors.Count}"));
    }

    [HttpPost("{id:guid}/analyze")]
    public async Task<IActionResult> Analyze(Guid id, CancellationToken ct)
    {
        try
        {
            var entity = await _intelligence.AnalyzeAndSaveAsync(id, ct);
            return Ok(ApiResponse.Ok(ToResponse(entity), "AI đã phân tích và gắn keyword"));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ApiResponse.Fail("NOT_FOUND", ex.Message));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail("MEDIA_ANALYSIS_FAILED", ex.Message));
        }
    }

    [HttpPost("analyze-all")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> AnalyzeAll(
        [FromQuery] bool force = false,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _intelligence.AnalyzeAllAsync(force, ct);
            return Ok(ApiResponse.Ok(
                result,
                $"Đã gắn nhãn {result.Analyzed} ảnh; bỏ qua {result.Skipped}; lỗi {result.Failed}"));
        }
        catch (OperationCanceledException)
        {
            return StatusCode(
                StatusCodes.Status408RequestTimeout,
                ApiResponse.Fail("MEDIA_ANALYSIS_CANCELLED", "Tiến trình gắn nhãn bị hủy"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail("MEDIA_ANALYSIS_ALL_FAILED", ex.Message));
        }
    }

    [HttpPost("move")]
    public async Task<IActionResult> Move(
        [FromBody] MoveMediaAssetsRequest request,
        CancellationToken ct)
    {
        try
        {
            var moved = await _repo.MoveAsync(request.Ids, request.FolderId, ct);
            return Ok(ApiResponse.Ok(new { moved }, $"Đã chuyển {moved} ảnh"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Fail("MEDIA_MOVE_FAILED", ex.Message));
        }
    }

    [HttpPost("recommend")]
    public async Task<IActionResult> Recommend(
        [FromBody] MediaRecommendationRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(ApiResponse.Ok(await _intelligence.RecommendAsync(request, ct)));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse.Fail("MEDIA_RECOMMEND_FAILED", ex.Message));
        }
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
