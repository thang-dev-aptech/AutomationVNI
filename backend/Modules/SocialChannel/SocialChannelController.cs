using Backend.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Modules.SocialChannel;

[ApiController]
[Route("api/[controller]")]
public class SocialChannelController
    : BaseController<SocialChannelModel, SocialChannelRepository,
        CreateSocialChannelRequest, UpdateSocialChannelRequest,
        SocialChannelFilterRequest, SocialChannelResponse>
{
    private readonly SocialChannelRepository _repo;

    public SocialChannelController(SocialChannelRepository repository) : base(repository)
        => _repo = repository;

    protected override string EntityLabel => "kênh mạng xã hội";
    protected override SocialChannelResponse ToResponse(SocialChannelModel e) => SocialChannelRepository.ToResponse(e);

    protected override async Task<SocialChannelModel> CreateEntityAsync(CreateSocialChannelRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PageName)) throw new ArgumentException("Tên page không được để trống");
        if (string.IsNullOrWhiteSpace(request.AccessToken)) throw new ArgumentException("Access token không được để trống");
        return await _repo.CreateAsync(request, ct);
    }

    protected override Task<SocialChannelModel?> UpdateEntityAsync(Guid id, UpdateSocialChannelRequest request, CancellationToken ct)
        => _repo.UpdateAsync(id, request, ct);

    protected override Task<PagedResult<SocialChannelResponse>> FilterEntitiesAsync(SocialChannelFilterRequest request, CancellationToken ct)
        => _repo.FilterAsync(request, ct);
}
