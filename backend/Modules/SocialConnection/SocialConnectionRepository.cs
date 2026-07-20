using Backend.Data;
using Backend.Modules.SocialChannel;
using Backend.Modules.SocialChannel.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.SocialConnection;

public class SocialConnectionRepository(
    AppDbContext context,
    IUserContext userContext,
    SocialChannelRepository socialChannelRepository) : GenericRepository<SocialConnectionModel>(context, userContext)
{
    public async Task<SocialConnectionModel> UpsertFromMetaAsync(
        string externalUserId,
        string displayName,
        string? avatarUrl,
        string? scopes,
        string? auditUser,
        CancellationToken ct = default)
        => await UpsertFromProviderAsync(
            SocialProvider.Meta, externalUserId, displayName, avatarUrl, scopes, auditUser, ct);

    /// <summary>
    /// Upsert the OAuth connection for a provider. Scoped by (Provider, ExternalUserId) so a Threads
    /// connection never collides with the Meta one — the two providers issue unrelated user ids.
    /// </summary>
    public async Task<SocialConnectionModel> UpsertFromProviderAsync(
        SocialProvider provider,
        string externalUserId,
        string displayName,
        string? avatarUrl,
        string? scopes,
        string? auditUser,
        CancellationToken ct = default)
    {
        var normalizedId = externalUserId.Trim();
        var actor = string.IsNullOrWhiteSpace(auditUser)
            ? $"{provider.ToString().ToLowerInvariant()}-oauth"
            : auditUser.Trim();
        var now = DateTime.UtcNow;

        // Prefer active row; otherwise revive the latest soft-deleted connection for this provider+user
        // so reconnect reuses one SocialConnectionId instead of orphaning old channels.
        var existing = await Context.Set<SocialConnectionModel>()
            .Where(x => x.Provider == provider && x.ExternalUserId == normalizedId)
            .OrderBy(x => x.IsDeleted)
            .ThenByDescending(x => x.LastSyncedAt ?? x.ConnectedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            existing.DisplayName = displayName.Trim();
            if (!string.IsNullOrWhiteSpace(avatarUrl))
                existing.AvatarUrl = avatarUrl;
            if (scopes is not null)
                existing.Scopes = scopes;
            existing.LastSyncedAt = now;
            existing.IsActive = true;
            existing.IsDeleted = false;
            existing.DeletedAt = null;
            existing.DeletedBy = null;
            existing.UpdatedAt = now;
            existing.UpdatedBy = actor;
            await Context.SaveChangesAsync(ct);
            return existing;
        }

        var entity = new SocialConnectionModel
        {
            Provider = provider,
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
        await socialChannelRepository.CleanupStaleChannelsAsync(ct);

        var connections = await QueryActive()
            .Where(c => c.IsActive)
            .OrderByDescending(x => x.LastSyncedAt ?? x.ConnectedAt)
            .ToListAsync(ct);

        var connectionIds = connections.Select(c => c.Id).ToList();
        var channels = await Context.Set<SocialChannelModel>()
            .Where(x => !x.IsDeleted
                && x.IsActive
                && x.SocialConnectionId != null
                && connectionIds.Contains(x.SocialConnectionId.Value))
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
        await Context.SaveChangesAsync(ct);

        await socialChannelRepository.SoftDeleteByConnectionAsync(
            id,
            GetCurrentUserName() ?? "disconnect",
            ct);

        // Self-heal: any leftover pages tied to other dead connections / inactive flags.
        await socialChannelRepository.CleanupStaleChannelsAsync(ct);
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
            ThreadsCount = kids.Count(c => c.ChannelType == SocialChannelType.Threads),
            Channels = kids.Select(SocialChannelRepository.ToResponse).ToList()
        };
    }
}
