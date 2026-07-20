using Backend.Data;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialConnection;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.SocialChannel;

public class SocialChannelRepository : GenericRepository<SocialChannelModel>
{
    public SocialChannelRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    /// <summary>
    /// Kênh dùng được để chọn đăng bài: đang active, chưa xóa, và thuộc connection Meta còn sống
    /// (hoặc kênh thủ công không gắn connection). Page của tài khoản đã disconnect không còn hiện.
    /// </summary>
    public override async Task<List<SocialChannelModel>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await CleanupStaleChannelsAsync(cancellationToken);

        var liveConnectionIds = await Context.Set<SocialConnectionModel>()
            .Where(c => !c.IsDeleted && c.IsActive)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        return await QueryActive()
            .Where(x => x.IsActive)
            .Where(x => x.SocialConnectionId == null
                        || liveConnectionIds.Contains(x.SocialConnectionId.Value))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<SocialChannelResponse>> FilterAsync(
        SocialChannelFilterRequest request, CancellationToken ct = default)
    {
        await CleanupStaleChannelsAsync(ct);

        var query = QueryActive().Where(x => x.IsActive);

        var liveConnectionIds = await Context.Set<SocialConnectionModel>()
            .Where(c => !c.IsDeleted && c.IsActive)
            .Select(c => c.Id)
            .ToListAsync(ct);

        query = query.Where(x => x.SocialConnectionId == null
                                 || liveConnectionIds.Contains(x.SocialConnectionId.Value));

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
                SocialPlatform.Threads => SocialChannelType.Threads,
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

        // Include soft-deleted rows so a previously pruned page can be revived on re-sync
        // instead of creating a duplicate.
        var existing = await Context.Set<SocialChannelModel>()
            .FirstOrDefaultAsync(
                x => x.Platform == platform
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
            existing.IsDeleted = false;
            existing.DeletedAt = null;
            existing.DeletedBy = null;
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

    /// <summary>
    /// Soft-delete Meta channels under a connection that are no longer returned by Graph
    /// (page deleted, permission revoked, IG unlinked, etc.). Clears tokens for safety.
    /// </summary>
    public async Task<int> SoftDeleteMissingFromMetaAsync(
        Guid socialConnectionId,
        IReadOnlyCollection<(SocialPlatform Platform, string ExternalPageId)> keepKeys,
        string? auditUser,
        CancellationToken ct = default)
    {
        var actor = string.IsNullOrWhiteSpace(auditUser) ? "meta-oauth" : auditUser.Trim();
        var keep = keepKeys
            .Select(k => (k.Platform, ExternalPageId: k.ExternalPageId.Trim()))
            .ToHashSet();

        var existing = await Context.Set<SocialChannelModel>()
            .Where(x => !x.IsDeleted && x.SocialConnectionId == socialConnectionId)
            .ToListAsync(ct);

        var removed = 0;
        var now = DateTime.UtcNow;
        foreach (var ch in existing)
        {
            if (keep.Contains((ch.Platform, ch.ExternalPageId)))
                continue;

            MarkChannelRemoved(ch, actor, now);
            removed++;
        }

        if (removed > 0)
            await Context.SaveChangesAsync(ct);

        return removed;
    }

    public async Task SoftDeleteByConnectionAsync(
        Guid socialConnectionId,
        string? auditUser,
        CancellationToken ct = default)
    {
        var actor = string.IsNullOrWhiteSpace(auditUser)
            ? GetCurrentUserName() ?? "disconnect"
            : auditUser.Trim();

        // Include inactive-but-not-deleted rows left by older disconnect logic.
        var channels = await Context.Set<SocialChannelModel>()
            .Where(x => !x.IsDeleted && x.SocialConnectionId == socialConnectionId)
            .ToListAsync(ct);

        if (channels.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var ch in channels)
            MarkChannelRemoved(ch, actor, now);

        await Context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Soft-delete channels that should no longer appear in pickers:
    /// inactive, or linked to a disconnected/deleted Meta account.
    /// </summary>
    public async Task<int> CleanupStaleChannelsAsync(CancellationToken ct = default)
    {
        var deadConnectionIds = await Context.Set<SocialConnectionModel>()
            .Where(c => c.IsDeleted || !c.IsActive)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var stale = await Context.Set<SocialChannelModel>()
            .Where(x => !x.IsDeleted && (
                !x.IsActive
                || (x.SocialConnectionId != null && deadConnectionIds.Contains(x.SocialConnectionId.Value))))
            .ToListAsync(ct);

        if (stale.Count == 0) return 0;

        var now = DateTime.UtcNow;
        const string actor = "stale-channel-cleanup";
        foreach (var ch in stale)
            MarkChannelRemoved(ch, actor, now);

        await Context.SaveChangesAsync(ct);
        return stale.Count;
    }

    private static void MarkChannelRemoved(SocialChannelModel ch, string actor, DateTime now)
    {
        ch.IsActive = false;
        ch.AccessToken = string.Empty;
        ch.RefreshToken = null;
        ch.TokenExpiresAt = null;
        ch.IsDeleted = true;
        ch.DeletedAt = now;
        ch.DeletedBy = actor;
        ch.UpdatedAt = now;
        ch.UpdatedBy = actor;
    }

    public async Task<List<SocialChannelModel>> GetOrphansAsync(CancellationToken ct = default)
    {
        return await QueryActive()
            .Where(x => x.IsActive && x.SocialConnectionId == null)
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
