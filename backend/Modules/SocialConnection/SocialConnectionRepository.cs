using Backend.Data;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.SocialConnection;

public class SocialConnectionRepository : GenericRepository<SocialConnectionModel>
{
    public SocialConnectionRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<SocialConnectionModel> UpsertFromMetaAsync(
        string externalUserId,
        string displayName,
        string? avatarUrl,
        string? scopes,
        string? auditUser,
        CancellationToken ct = default)
    {
        var normalizedId = externalUserId.Trim();
        var actor = string.IsNullOrWhiteSpace(auditUser) ? "meta-oauth" : auditUser.Trim();
        var now = DateTime.UtcNow;

        var existing = await Context.Set<SocialConnectionModel>()
            .FirstOrDefaultAsync(
                x => !x.IsDeleted
                    && x.Provider == SocialProvider.Meta
                    && x.ExternalUserId == normalizedId,
                ct);

        if (existing is not null)
        {
            existing.DisplayName = displayName.Trim();
            if (!string.IsNullOrWhiteSpace(avatarUrl))
                existing.AvatarUrl = avatarUrl;
            if (scopes is not null)
                existing.Scopes = scopes;
            existing.LastSyncedAt = now;
            existing.IsActive = true;
            existing.UpdatedAt = now;
            existing.UpdatedBy = actor;
            await Context.SaveChangesAsync(ct);
            return existing;
        }

        var entity = new SocialConnectionModel
        {
            Provider = SocialProvider.Meta,
            ExternalUserId = normalizedId,
            DisplayName = displayName.Trim(),
            AvatarUrl = avatarUrl,
            Scopes = scopes,
            ConnectedAt = now,
            LastSyncedAt = now,
            IsActive = true
        };

        ApplyCreateAudit(entity);
        entity.CreatedBy = actor;
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();

        DbSet.Add(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<List<SocialConnectionResponse>> GetWithChannelsAsync(CancellationToken ct = default)
    {
        var connections = await QueryActive()
            .OrderByDescending(x => x.LastSyncedAt ?? x.ConnectedAt)
            .ToListAsync(ct);

        var connectionIds = connections.Select(c => c.Id).ToList();
        var channels = await Context.Set<SocialChannelModel>()
            .Where(x => !x.IsDeleted && x.SocialConnectionId != null && connectionIds.Contains(x.SocialConnectionId.Value))
            .OrderBy(x => x.ChannelType)
            .ThenBy(x => x.PageName)
            .ToListAsync(ct);

        return connections.Select(c =>
        {
            var kids = channels.Where(ch => ch.SocialConnectionId == c.Id).ToList();
            return ToResponse(c, kids);
        }).ToList();
    }

    public async Task<bool> SoftDisconnectAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return false;

        entity.IsActive = false;
        ApplySoftDeleteAudit(entity);

        var channels = await Context.Set<SocialChannelModel>()
            .Where(x => !x.IsDeleted && x.SocialConnectionId == id)
            .ToListAsync(ct);

        foreach (var ch in channels)
        {
            ch.IsActive = false;
            ch.UpdatedAt = DateTime.UtcNow;
            ch.UpdatedBy = GetCurrentUserName() ?? "disconnect";
        }

        await Context.SaveChangesAsync(ct);
        return true;
    }

    public static SocialConnectionResponse ToResponse(
        SocialConnectionModel e,
        IReadOnlyList<SocialChannelModel>? channels = null)
    {
        var kids = channels ?? [];
        return new SocialConnectionResponse
        {
            Id = e.Id,
            Provider = e.Provider,
            ExternalUserId = e.ExternalUserId,
            DisplayName = e.DisplayName,
            AvatarUrl = e.AvatarUrl,
            ConnectedAt = e.ConnectedAt,
            LastSyncedAt = e.LastSyncedAt,
            IsActive = e.IsActive,
            PageCount = kids.Count(c => c.ChannelType == SocialChannelType.Page),
            InstagramCount = kids.Count(c => c.ChannelType == SocialChannelType.Instagram),
            GroupCount = kids.Count(c => c.ChannelType == SocialChannelType.Group),
            Channels = kids.Select(SocialChannelRepository.ToResponse).ToList()
        };
    }
}
