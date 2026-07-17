using Backend.Data;
using Backend.Modules.SocialChannel.Enums;
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

        if (request.ChannelType.HasValue)
            query = query.Where(x => x.ChannelType == request.ChannelType.Value);

        if (request.SocialConnectionId.HasValue)
            query = query.Where(x => x.SocialConnectionId == request.SocialConnectionId.Value);

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
        var channelType = request.ChannelType;
        if (channelType == default)
        {
            channelType = request.Platform switch
            {
                SocialPlatform.Instagram => SocialChannelType.Instagram,
                _ => SocialChannelType.Page
            };
        }

        var entity = new SocialChannelModel
        {
            Platform = request.Platform,
            ChannelType = channelType,
            PageName = request.PageName.Trim(),
            ExternalPageId = request.ExternalPageId.Trim(),
            AccessToken = request.AccessToken,
            RefreshToken = request.RefreshToken,
            TokenExpiresAt = request.TokenExpiresAt,
            SocialConnectionId = request.SocialConnectionId,
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

    public async Task<SocialChannelModel> UpsertFromMetaAsync(
        SocialPlatform platform,
        SocialChannelType channelType,
        string externalPageId,
        string pageName,
        string accessToken,
        DateTime? tokenExpiresAt,
        Guid socialConnectionId,
        string? extraJson,
        string? auditUser,
        CancellationToken ct = default)
    {
        var normalizedId = externalPageId.Trim();
        var actor = string.IsNullOrWhiteSpace(auditUser) ? "meta-oauth" : auditUser.Trim();

        var existing = await Context.Set<SocialChannelModel>()
            .FirstOrDefaultAsync(
                x => !x.IsDeleted
                    && x.Platform == platform
                    && x.ExternalPageId == normalizedId,
                ct);

        if (existing is not null)
        {
            existing.PageName = pageName.Trim();
            existing.AccessToken = accessToken;
            existing.TokenExpiresAt = tokenExpiresAt;
            existing.ChannelType = channelType;
            existing.SocialConnectionId = socialConnectionId;
            existing.IsActive = true;
            if (extraJson is not null)
                existing.ExtraJson = extraJson;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = actor;
            await Context.SaveChangesAsync(ct);
            return existing;
        }

        var entity = new SocialChannelModel
        {
            Platform = platform,
            ChannelType = channelType,
            PageName = pageName.Trim(),
            ExternalPageId = normalizedId,
            AccessToken = accessToken,
            TokenExpiresAt = tokenExpiresAt,
            SocialConnectionId = socialConnectionId,
            IsActive = true,
            ExtraJson = extraJson
        };

        ApplyCreateAudit(entity);
        entity.CreatedBy = actor;
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();

        DbSet.Add(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<List<SocialChannelModel>> GetOrphansAsync(CancellationToken ct = default)
    {
        return await QueryActive()
            .Where(x => x.SocialConnectionId == null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public static SocialChannelResponse ToResponse(SocialChannelModel e) => new()
    {
        Id = e.Id,
        Platform = e.Platform,
        ChannelType = e.ChannelType,
        PageName = e.PageName,
        ExternalPageId = e.ExternalPageId,
        SocialConnectionId = e.SocialConnectionId,
        ExtraJson = e.ExtraJson,
        TokenExpiresAt = e.TokenExpiresAt,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt
    };
}
