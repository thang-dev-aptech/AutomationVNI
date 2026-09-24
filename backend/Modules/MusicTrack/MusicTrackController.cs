using Backend.Shared;
using Backend.Shared.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.MusicTrack;

[ApiController]
[Route("api/[controller]")]
public class MusicTrackController
    : BaseController<MusicTrackModel, MusicTrackRepository,
        CreateMusicTrackRequest, UpdateMusicTrackRequest,
        MusicTrackFilterRequest, MusicTrackResponse>
{
    private readonly MusicTrackRepository _repo;
    private readonly IFileStorageService _fileStorage;

    public MusicTrackController(
        MusicTrackRepository repository,
        IFileStorageService fileStorage) : base(repository)
    {
        _repo = repository;
        _fileStorage = fileStorage;
    }

    protected override string EntityLabel => "bài nhạc";
    protected override MusicTrackResponse ToResponse(MusicTrackModel e) => MusicTrackRepository.ToResponse(e);

    protected override async Task<MusicTrackModel> CreateEntityAsync(CreateMusicTrackRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StoragePath)) throw new ArgumentException("StoragePath không được để trống");
        return await _repo.CreateAsync(request, ct);
    }

    protected override Task<MusicTrackModel?> UpdateEntityAsync(Guid id, UpdateMusicTrackRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [Authorize(Roles = "Admin,ContentManager")]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] string? displayName,
        CancellationToken ct)
    {
        var saveResult = await _fileStorage.SaveAsync(file, "music", ct);
        var entity = await _repo.CreateFromUploadAsync(saveResult, displayName, ct);
        return CreatedAtAction(
            nameof(GetById),
            new { id = entity.Id },
            ApiResponse.Ok(ToResponse(entity), "Upload nhạc thành công"));
    }

    [HttpGet("{id:guid}/preview")]
    [AllowAnonymous]
    public async Task<IActionResult> Preview(Guid id, CancellationToken ct)
    {
        var entity = await _repo.GetByIdAsync(id, ct);
        if (entity is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài nhạc"));

        if (string.IsNullOrWhiteSpace(entity.StoragePath)
            || !await _fileStorage.ExistsAsync(entity.StoragePath, ct))
            return NotFound(ApiResponse.Fail("FILE_NOT_FOUND", "File không tồn tại trên storage"));

        var stream = await _fileStorage.OpenReadAsync(entity.StoragePath, ct);
        return File(stream, entity.MimeType, enableRangeProcessing: true);
    }
}
