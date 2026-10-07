using Backend.Data;
using Backend.Modules.ChannelGroup;
using Backend.Modules.MediaAsset;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.SocialChannel;
using Backend.Shared;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Campaign;

/// <summary>
/// Đọc chiến dịch → page → bài với truy vấn gom nhóm (không N+1).
/// </summary>
public sealed class CampaignQueryService(
    AppDbContext context,
    CampaignRepository campaignRepository,
    TimeProvider timeProvider)
{
    private static readonly PostStatus[] UpcomingStatuses =
    [
        PostStatus.Queued,
        PostStatus.Approved,
        PostStatus.Scheduled,
    ];

    private const int ContentSnippetMax = 200;

    public async Task<List<CampaignSummaryResponse>> ListSummariesAsync(CancellationToken ct = default)
    {
        var campaigns = await context.Set<CampaignModel>()
            .AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Name)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.MediaType,
                c.ScheduleMode,
                c.Status,
                c.StartDate,
                c.EndDate,
                c.CreatedAt,
            })
            .ToListAsync(ct);

        if (campaigns.Count == 0) return [];

        var ids = campaigns.Select(c => c.Id).ToList();
        var pagesByCampaign = await ResolveRunningPagesByCampaignAsync(ids, ct);
        var statsByCampaign = await AggregatePostStatsByCampaignAsync(ids, ct);

        return campaigns.Select(c =>
        {
            statsByCampaign.TryGetValue(c.Id, out var stats);
            pagesByCampaign.TryGetValue(c.Id, out var pages);
            return new CampaignSummaryResponse
            {
                Id = c.Id,
                Name = c.Name,
                MediaType = c.MediaType,
                ScheduleMode = c.ScheduleMode,
                Status = c.Status,
                StartDate = c.StartDate,
                EndDate = c.EndDate,
                CreatedAt = c.CreatedAt,
                PageCount = pages?.Count ?? 0,
                UpcomingCount = stats?.Upcoming ?? 0,
                PublishedCount = stats?.Published ?? 0,
                FailedCount = stats?.Failed ?? 0,
            };
        }).ToList();
    }

    public async Task<CampaignDetailResponse?> GetDetailAsync(Guid campaignId, CancellationToken ct = default)
    {
        var campaign = await campaignRepository.GetResponseByIdAsync(campaignId, ct);
        if (campaign is null) return null;

        var entity = await context.Set<CampaignModel>()
            .AsNoTracking()
            .Where(c => c.Id == campaignId && !c.IsDeleted)
            .Select(c => new { c.ExtraJson })
            .FirstOrDefaultAsync(ct);
        if (entity is null) return null;

        var pageIds = await ResolveRunningPagesByCampaignAsync([campaignId], ct);
        var channelIds = pageIds.GetValueOrDefault(campaignId) ?? [];
        var warnings = CampaignGenerationService.ReadWarnings(entity.ExtraJson)
            .ToDictionary(w => w.ChannelId, w => w.Message);

        var channelNames = channelIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await context.Set<SocialChannelModel>()
                .AsNoTracking()
                .Where(c => channelIds.Contains(c.Id))
                .Select(c => new { c.Id, c.PageName })
                .ToDictionaryAsync(x => x.Id, x => x.PageName, ct);

        var pageStats = await AggregatePostStatsByPageAsync(campaignId, channelIds, ct);
        var nextByPage = await NextScheduledByPageAsync(campaignId, channelIds, ct);

        var pages = channelIds
            .OrderBy(id => channelNames.GetValueOrDefault(id) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(id => id)
            .Select(id =>
            {
                pageStats.TryGetValue(id, out var s);
                nextByPage.TryGetValue(id, out var next);
                warnings.TryGetValue(id, out var warning);
                return new CampaignPageStatsResponse
                {
                    SocialChannelId = id,
                    ChannelName = channelNames.GetValueOrDefault(id) ?? string.Empty,
                    ScheduledCount = s?.Upcoming ?? 0,
                    PublishedCount = s?.Published ?? 0,
                    FailedCount = s?.Failed ?? 0,
                    NextScheduledAt = next,
                    Warning = warning,
                };
            })
            .ToList();

        return new CampaignDetailResponse
        {
            Campaign = campaign,
            Pages = pages,
        };
    }

    public async Task<PagedResult<CampaignPagePostItemResponse>?> ListPagePostsAsync(
        Guid campaignId,
        Guid channelId,
        CampaignPagePostsRequest request,
        CancellationToken ct = default)
    {
        var exists = await context.Set<CampaignModel>()
            .AsNoTracking()
            .AnyAsync(c => c.Id == campaignId && !c.IsDeleted, ct);
        if (!exists) return null;

        var index = request.Index < 1 ? 1 : request.Index;
        var size = request.Size < 1 ? 20 : Math.Min(request.Size, 100);

        var baseQuery = context.Set<PostModel>()
            .AsNoTracking()
            .Where(p => !p.IsDeleted
                        && p.CampaignId == campaignId
                        && p.SocialChannelId == channelId);

        var total = await baseQuery.CountAsync(ct);

        var pageRows = await baseQuery
            .OrderByDescending(p => p.ScheduledPublishAt ?? p.PublishedAt ?? p.CreatedAt)
            .ThenByDescending(p => p.CreatedAt)
            .Skip((index - 1) * size)
            .Take(size)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Content,
                p.Status,
                p.ScheduledPublishAt,
                p.PublishedAt,
            })
            .ToListAsync(ct);

        var postIds = pageRows.Select(p => p.Id).ToList();
        var mediaAgg = postIds.Count == 0
            ? []
            : await (
                from pm in context.Set<PostMediaModel>().AsNoTracking()
                where !pm.IsDeleted && postIds.Contains(pm.PostId)
                join m in context.Set<MediaAssetModel>().AsNoTracking()
                    on pm.MediaId equals m.Id
                where !m.IsDeleted
                select new { pm.PostId, pm.MediaId, pm.SortOrder }
            ).ToListAsync(ct);

        var mediaByPost = mediaAgg
            .GroupBy(x => x.PostId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var ordered = g.OrderBy(x => x.SortOrder).ThenBy(x => x.MediaId).ToList();
                    return (Count: ordered.Count, Primary: ordered.FirstOrDefault()?.MediaId);
                });

        var items = pageRows.Select(p =>
        {
            mediaByPost.TryGetValue(p.Id, out var media);
            Guid? primary = media.Primary;
            return new CampaignPagePostItemResponse
            {
                Id = p.Id,
                Title = p.Title,
                ContentSnippet = Truncate(p.Content, ContentSnippetMax),
                Status = p.Status,
                ScheduledPublishAt = p.ScheduledPublishAt,
                PublishedAt = p.PublishedAt,
                MediaCount = media.Count,
                PrimaryMediaId = primary,
                ThumbnailUrl = primary is Guid mid ? MediaAssetUrls.Preview(mid) : null,
            };
        }).ToList();

        return new PagedResult<CampaignPagePostItemResponse>
        {
            Items = items,
            Total = total,
            Index = index,
            Size = size,
        };
    }

    private async Task<Dictionary<Guid, List<Guid>>> ResolveRunningPagesByCampaignAsync(
        IReadOnlyList<Guid> campaignIds, CancellationToken ct)
    {
        if (campaignIds.Count == 0) return [];

        var direct = await context.Set<CampaignChannelModel>()
            .AsNoTracking()
            .Where(x => !x.IsDeleted && campaignIds.Contains(x.CampaignId))
            .Select(x => new { x.CampaignId, x.SocialChannelId })
            .ToListAsync(ct);

        var groupLinks = await context.Set<CampaignChannelGroupModel>()
            .AsNoTracking()
            .Where(x => !x.IsDeleted && campaignIds.Contains(x.CampaignId))
            .Select(x => new { x.CampaignId, x.ChannelGroupId })
            .ToListAsync(ct);

        var groupIds = groupLinks.Select(x => x.ChannelGroupId).Distinct().ToList();
        List<(Guid GroupId, Guid ChannelId)> members = [];
        if (groupIds.Count > 0)
        {
            var activeGroups = await context.Set<ChannelGroupModel>()
                .AsNoTracking()
                .Where(g => !g.IsDeleted && groupIds.Contains(g.Id))
                .Select(g => g.Id)
                .ToListAsync(ct);

            if (activeGroups.Count > 0)
            {
                var memberRows = await context.Set<ChannelGroupMemberModel>()
                    .AsNoTracking()
                    .Where(m => !m.IsDeleted && activeGroups.Contains(m.ChannelGroupId))
                    .Select(m => new { m.ChannelGroupId, m.SocialChannelId })
                    .ToListAsync(ct);
                members = memberRows
                    .Select(m => (GroupId: m.ChannelGroupId, ChannelId: m.SocialChannelId))
                    .ToList();
            }
        }

        var membersByGroup = members
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ChannelId).ToList());

        var unionByCampaign = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var id in campaignIds)
            unionByCampaign[id] = [];

        foreach (var row in direct)
            unionByCampaign[row.CampaignId].Add(row.SocialChannelId);

        foreach (var link in groupLinks)
        {
            if (!membersByGroup.TryGetValue(link.ChannelGroupId, out var chans)) continue;
            foreach (var ch in chans)
                unionByCampaign[link.CampaignId].Add(ch);
        }

        var allChannelIds = unionByCampaign.Values.SelectMany(x => x).Distinct().ToList();
        HashSet<Guid> alive = [];
        if (allChannelIds.Count > 0)
        {
            alive = (await context.Set<SocialChannelModel>()
                .AsNoTracking()
                .Where(c => !c.IsDeleted && allChannelIds.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync(ct)).ToHashSet();
        }

        return unionByCampaign.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Where(alive.Contains).Distinct().ToList());
    }

    private async Task<Dictionary<Guid, CampaignAgg>> AggregatePostStatsByCampaignAsync(
        IReadOnlyList<Guid> campaignIds, CancellationToken ct)
    {
        if (campaignIds.Count == 0) return [];

        var rows = await context.Set<PostModel>()
            .AsNoTracking()
            .Where(p => !p.IsDeleted
                        && p.CampaignId != null
                        && campaignIds.Contains(p.CampaignId.Value))
            .GroupBy(p => new { CampaignId = p.CampaignId!.Value, p.Status })
            .Select(g => new { g.Key.CampaignId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, CampaignAgg>();
        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.CampaignId, out var agg))
            {
                agg = new CampaignAgg();
                result[row.CampaignId] = agg;
            }

            if (UpcomingStatuses.Contains(row.Status))
                agg.Upcoming += row.Count;
            else if (row.Status == PostStatus.Published)
                agg.Published += row.Count;
            else if (row.Status == PostStatus.Failed)
                agg.Failed += row.Count;
        }

        return result;
    }

    private async Task<Dictionary<Guid, CampaignAgg>> AggregatePostStatsByPageAsync(
        Guid campaignId, IReadOnlyList<Guid> channelIds, CancellationToken ct)
    {
        if (channelIds.Count == 0) return [];

        var rows = await context.Set<PostModel>()
            .AsNoTracking()
            .Where(p => !p.IsDeleted
                        && p.CampaignId == campaignId
                        && channelIds.Contains(p.SocialChannelId))
            .GroupBy(p => new { p.SocialChannelId, p.Status })
            .Select(g => new { g.Key.SocialChannelId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, CampaignAgg>();
        foreach (var row in rows)
        {
            if (!result.TryGetValue(row.SocialChannelId, out var agg))
            {
                agg = new CampaignAgg();
                result[row.SocialChannelId] = agg;
            }

            if (UpcomingStatuses.Contains(row.Status))
                agg.Upcoming += row.Count;
            else if (row.Status == PostStatus.Published)
                agg.Published += row.Count;
            else if (row.Status == PostStatus.Failed)
                agg.Failed += row.Count;
        }

        return result;
    }

    private async Task<Dictionary<Guid, DateTime?>> NextScheduledByPageAsync(
        Guid campaignId, IReadOnlyList<Guid> channelIds, CancellationToken ct)
    {
        if (channelIds.Count == 0) return [];

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var rows = await context.Set<PostModel>()
            .AsNoTracking()
            .Where(p => !p.IsDeleted
                        && p.CampaignId == campaignId
                        && channelIds.Contains(p.SocialChannelId)
                        && p.ScheduledPublishAt != null
                        && p.ScheduledPublishAt >= now
                        && UpcomingStatuses.Contains(p.Status))
            .GroupBy(p => p.SocialChannelId)
            .Select(g => new
            {
                ChannelId = g.Key,
                Next = g.Min(p => p.ScheduledPublishAt),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(x => x.ChannelId, x => x.Next);
    }

    private static string? Truncate(string? content, int max)
    {
        if (string.IsNullOrEmpty(content)) return content;
        var trimmed = content.Trim();
        if (trimmed.Length <= max) return trimmed;
        return trimmed[..max];
    }

    private sealed class CampaignAgg
    {
        public int Upcoming;
        public int Published;
        public int Failed;
    }
}
