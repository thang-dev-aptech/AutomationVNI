using Backend.Data;
using Backend.Modules.SocialChannel;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.ChannelGroup;

public class ChannelGroupRepository : GenericRepository<ChannelGroupModel>
{
    public ChannelGroupRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<List<ChannelGroupResponse>> GetAllResponsesAsync(CancellationToken ct = default)
    {
        var groups = await QueryActive()
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
        return await MapResponsesAsync(groups, ct);
    }

    public async Task<ChannelGroupResponse?> GetResponseByIdAsync(Guid id, CancellationToken ct = default)
    {
        var group = await GetByIdAsync(id, ct);
        if (group is null) return null;
        var list = await MapResponsesAsync([group], ct);
        return list[0];
    }

    public async Task<PagedResult<ChannelGroupResponse>> FilterAsync(
        ChannelGroupFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(x => x.Name.Contains(kw)
                || (x.Description != null && x.Description.Contains(kw)));
        }

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        var items = await MapResponsesAsync(paged.Items, ct);
        return new PagedResult<ChannelGroupResponse>
        {
            Items = items,
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<ChannelGroupModel> CreateAsync(
        CreateChannelGroupRequest request, CancellationToken ct = default)
    {
        var name = RequireName(request.Name);
        await EnsureNameUniqueAsync(name, excludeId: null, ct);

        var channelIds = NormalizeChannelIds(request.ChannelIds);
        await EnsureChannelsExistAsync(channelIds, ct);

        var entity = new ChannelGroupModel
        {
            Name = name,
            Description = TrimOrNull(request.Description)
        };
        await base.CreateAsync(entity, ct);
        await ReplaceMembersAsync(entity.Id, channelIds, ct);
        return entity;
    }

    public async Task<ChannelGroupModel?> UpdateAsync(
        Guid id, UpdateChannelGroupRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.Name is not null)
        {
            var name = RequireName(request.Name);
            await EnsureNameUniqueAsync(name, excludeId: id, ct);
            entity.Name = name;
        }

        if (request.Description is not null)
            entity.Description = TrimOrNull(request.Description);

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);

        if (request.ChannelIds is not null)
        {
            var channelIds = NormalizeChannelIds(request.ChannelIds);
            await EnsureChannelsExistAsync(channelIds, ct);
            await ReplaceMembersAsync(id, channelIds, ct);
        }

        return entity;
    }

    public override async Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken);
        if (entity is null) return false;

        ApplySoftDeleteAudit(entity);

        var members = await Context.Set<ChannelGroupMemberModel>()
            .Where(m => !m.IsDeleted && m.ChannelGroupId == id)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var actor = GetCurrentUserName();
        foreach (var member in members)
        {
            member.IsDeleted = true;
            member.DeletedAt = now;
            member.DeletedBy = actor;
        }

        await Context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Quy tập kênh từ các nhóm còn sống (chưa xoá) — bỏ kênh đã xoá mềm.
    /// Dùng cho bộ lọc lịch khi chọn Nhóm kênh.
    /// </summary>
    public async Task<List<Guid>> ResolveChannelIdsAsync(
        IEnumerable<Guid>? groupIds, CancellationToken ct = default)
    {
        var ids = (groupIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return [];

        var activeGroupIds = await QueryActive()
            .Where(g => ids.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync(ct);
        if (activeGroupIds.Count == 0) return [];

        var memberChannelIds = await Context.Set<ChannelGroupMemberModel>()
            .Where(m => !m.IsDeleted && activeGroupIds.Contains(m.ChannelGroupId))
            .Select(m => m.SocialChannelId)
            .Distinct()
            .ToListAsync(ct);
        if (memberChannelIds.Count == 0) return [];

        return await Context.Set<SocialChannelModel>()
            .Where(c => !c.IsDeleted && memberChannelIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
    }

    private async Task ReplaceMembersAsync(
        Guid groupId, List<Guid> channelIds, CancellationToken ct)
    {
        var existing = await Context.Set<ChannelGroupMemberModel>()
            .Where(m => m.ChannelGroupId == groupId)
            .ToListAsync(ct);

        var keep = channelIds.ToHashSet();
        var now = DateTime.UtcNow;
        var actor = GetCurrentUserName();
        foreach (var member in existing.Where(m => !m.IsDeleted && !keep.Contains(m.SocialChannelId)))
        {
            member.IsDeleted = true;
            member.DeletedAt = now;
            member.DeletedBy = actor;
        }

        foreach (var channelId in channelIds)
        {
            var row = existing.FirstOrDefault(m => m.SocialChannelId == channelId);
            if (row is null)
            {
                var created = new ChannelGroupMemberModel
                {
                    Id = Guid.NewGuid(),
                    ChannelGroupId = groupId,
                    SocialChannelId = channelId,
                    CreatedAt = now,
                    CreatedBy = actor,
                    IsDeleted = false
                };
                Context.Set<ChannelGroupMemberModel>().Add(created);
            }
            else if (row.IsDeleted)
            {
                row.IsDeleted = false;
                row.DeletedAt = null;
                row.DeletedBy = null;
                row.UpdatedAt = now;
                row.UpdatedBy = actor;
            }
        }

        await Context.SaveChangesAsync(ct);
    }

    private async Task EnsureChannelsExistAsync(List<Guid> channelIds, CancellationToken ct)
    {
        if (channelIds.Count == 0) return;

        var found = await Context.Set<SocialChannelModel>()
            .Where(c => !c.IsDeleted && channelIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
        var missing = channelIds.Except(found).ToList();
        if (missing.Count > 0)
            throw new ArgumentException("Có kênh không tồn tại hoặc đã bị xoá.");
    }

    private async Task EnsureNameUniqueAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var lower = name.ToLowerInvariant();
        var taken = await QueryActive()
            .Where(x => excludeId == null || x.Id != excludeId.Value)
            .AnyAsync(x => x.Name.ToLower() == lower, ct);
        if (taken)
            throw new ArgumentException($"Tên nhóm '{name}' đã tồn tại.");
    }

    private async Task<List<ChannelGroupResponse>> MapResponsesAsync(
        List<ChannelGroupModel> groups, CancellationToken ct)
    {
        if (groups.Count == 0) return [];

        var groupIds = groups.Select(g => g.Id).ToList();
        var memberRows = await Context.Set<ChannelGroupMemberModel>()
            .Where(m => !m.IsDeleted && groupIds.Contains(m.ChannelGroupId))
            .Select(m => new { m.ChannelGroupId, m.SocialChannelId })
            .ToListAsync(ct);

        var channelIds = memberRows.Select(m => m.SocialChannelId).Distinct().ToList();
        var channels = channelIds.Count == 0
            ? []
            : await Context.Set<SocialChannelModel>()
                .Where(c => !c.IsDeleted && channelIds.Contains(c.Id))
                .OrderByVniFirst()
                .ToListAsync(ct);

        var channelById = channels.ToDictionary(c => c.Id);
        var orderedIds = channels.Select(c => c.Id).ToList();

        return groups.Select(g =>
        {
            var memberIds = memberRows
                .Where(m => m.ChannelGroupId == g.Id)
                .Select(m => m.SocialChannelId)
                .Where(channelById.ContainsKey)
                .OrderBy(id => orderedIds.IndexOf(id))
                .ToList();

            var channelResponses = memberIds.Select(id =>
            {
                var c = channelById[id];
                return new ChannelGroupChannelResponse
                {
                    Id = c.Id,
                    PageName = c.PageName,
                    Platform = c.Platform,
                    ChannelType = c.ChannelType,
                    ExternalPageId = c.ExternalPageId
                };
            }).ToList();

            return new ChannelGroupResponse
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                ChannelCount = channelResponses.Count,
                Channels = channelResponses,
                CreatedAt = g.CreatedAt
            };
        }).ToList();
    }

    private static List<Guid> NormalizeChannelIds(IEnumerable<Guid>? channelIds)
        => (channelIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

    private static string RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tên nhóm không được để trống");
        return name.Trim();
    }

    private static string? TrimOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
