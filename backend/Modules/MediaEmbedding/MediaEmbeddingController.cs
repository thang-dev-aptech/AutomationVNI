using Backend.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.MediaEmbedding;

[ApiController]
[Route("api/[controller]")]
public class MediaEmbeddingController
    : BaseController<MediaEmbeddingModel, MediaEmbeddingRepository,
        CreateMediaEmbeddingRequest, UpdateMediaEmbeddingRequest,
        MediaEmbeddingFilterRequest, MediaEmbeddingResponse>
{
    private readonly MediaEmbeddingRepository _repo;

    public MediaEmbeddingController(MediaEmbeddingRepository repository) : base(repository)
        => _repo = repository;

    protected override string EntityLabel => "media embedding";
    protected override MediaEmbeddingResponse ToResponse(MediaEmbeddingModel e) => MediaEmbeddingRepository.ToResponse(e);

    protected override Task<MediaEmbeddingModel> CreateEntityAsync(CreateMediaEmbeddingRequest request, CancellationToken ct)
        => _repo.CreateAsync(request, ct);

    protected override Task<MediaEmbeddingModel?> UpdateEntityAsync(Guid id, UpdateMediaEmbeddingRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<MediaEmbeddingResponse>> FilterEntitiesAsync(MediaEmbeddingFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);

    [HttpGet("by-media/{mediaAssetId:guid}")]
    public async Task<IActionResult> GetByMediaAsset(Guid mediaAssetId, CancellationToken ct)
    {
        var entity = await _repo.GetByMediaAssetAsync(mediaAssetId, ct);
        if (entity is null) return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy embedding cho media này"));
        return Ok(ApiResponse.Ok(MediaEmbeddingRepository.ToResponse(entity)));
    }
}
