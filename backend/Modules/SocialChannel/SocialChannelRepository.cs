using Backend.Data;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.SocialChannel;

public class SocialChannelRepository : GenericRepository<SocialChannelModel>
{
    public SocialChannelRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<SocialChannelResponse>> FilterAsync(
        SocialChannelFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(x => x.PageName.Contains(kw) || x.ExternalPageId.Contains(kw));
        }

        if (request.Platform.HasValue)
            query = query.Where(x => x.Platform == request.Platform.Value);

        if (request.IsActive.HasValue)
            query = query.Where(x => x.IsActive == request.IsActive.Value);

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        return new PagedResult<SocialChannelResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<SocialChannelModel> CreateAsync(
        CreateSocialChannelRequest request, CancellationToken ct = default)
    {
        var entity = new SocialChannelModel
        {
            Platform = request.Platform,
            PageName = request.PageName.Trim(),
            ExternalPageId = request.ExternalPageId.Trim(),
            AccessToken = request.AccessToken,
            RefreshToken = request.RefreshToken,
            TokenExpiresAt = request.TokenExpiresAt,
            IsActive = true
        };

        return await base.CreateAsync(entity, ct);
    }

    public async Task<SocialChannelModel?> UpdateAsync(
        Guid id, UpdateSocialChannelRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.PageName is not null) entity.PageName = request.PageName.Trim();
        if (request.AccessToken is not null) entity.AccessToken = request.AccessToken;
        if (request.RefreshToken is not null) entity.RefreshToken = request.RefreshToken;
        if (request.TokenExpiresAt.HasValue) entity.TokenExpiresAt = request.TokenExpiresAt;
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public static SocialChannelResponse ToResponse(SocialChannelModel e) => new()
    {
        Id = e.Id,
        Platform = e.Platform,
        PageName = e.PageName,
        ExternalPageId = e.ExternalPageId,
        TokenExpiresAt = e.TokenExpiresAt,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt
    };
}
