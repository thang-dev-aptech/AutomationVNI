using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Modules.Campaign.Enums;
using Backend.Modules.ChannelGroup;
using Backend.Modules.Post;
using Backend.Modules.Post.Enums;
using Backend.Modules.SocialChannel;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Campaign;

public class CampaignRepository : GenericRepository<CampaignModel>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex TimeRegex = new(
        @"^([01]\d|2[0-3]):([0-5]\d)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly PostStatus[] CancellableStatuses =
    [
        PostStatus.Scheduled,
        PostStatus.Approved,
        PostStatus.Queued,
    ];

    public CampaignRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<List<CampaignResponse>> GetAllResponsesAsync(CancellationToken ct = default)
    {
        var items = await QueryActive()
            .OrderByDescending(x => x.CreatedAt)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
        return await MapResponsesAsync(items, ct);
    }

    public async Task<CampaignResponse?> GetResponseByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;
        var list = await MapResponsesAsync([entity], ct);
        return list[0];
    }

    public async Task<PagedResult<CampaignResponse>> FilterAsync(
        CampaignFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();
        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(x => x.Name.Contains(kw));
        }

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        var items = await MapResponsesAsync(paged.Items, ct);
        return new PagedResult<CampaignResponse>
        {
            Items = items,
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size,
        };
    }

    public async Task<CampaignModel> CreateAsync(
        CreateCampaignRequest request, CancellationToken ct = default)
    {
        var name = RequireName(request.Name);
        var channelIds = NormalizeGuids(request.ChannelIds);
        var groupIds = NormalizeGuids(request.ChannelGroupIds);
        if (channelIds.Count == 0 && groupIds.Count == 0)
            throw new ArgumentException("Cần chọn ít nhất một kênh hoặc nhóm kênh.");

        await EnsureChannelsExistAsync(channelIds, ct);
        await EnsureGroupsExistAsync(groupIds, ct);

        var weekdays = NormalizeWeekdays(request.ScheduleMode, request.Weekdays);
        var times = NormalizePublishTimes(request.PublishTimes);
        var jitter = RequireJitter(request.JitterMinutes);
        var start = NormalizeDate(request.StartDate, "Ngày bắt đầu");
        var end = NormalizeOptionalEnd(request.EndDate, start);
        var (mediaType, imageStrategy) = NormalizeMedia(
            request.MediaType, request.ImageStrategy);

        var entity = new CampaignModel
        {
            Name = name,
            MediaType = mediaType,
            ImageStrategy = imageStrategy,
            ScheduleMode = request.ScheduleMode,
            WeekdaysJson = Serialize(weekdays),
            PublishTimesJson = Serialize(times),
            JitterMinutes = jitter,
            StartDate = start,
            EndDate = end,
            Status = CampaignStatus.Running,
        };

        await using var tx = await Context.Database.BeginTransactionAsync(ct);
        try
        {
            await base.CreateAsync(entity, ct);
            await ReplaceChannelsAsync(entity.Id, channelIds, ct);
            await ReplaceGroupsAsync(entity.Id, groupIds, ct);
            await Context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        return entity;
    }

    public async Task<CampaignModel?> UpdateAsync(
        Guid id, UpdateCampaignRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;
        if (entity.Status == CampaignStatus.Ended)
            throw new ArgumentException("Chiến dịch đã kết thúc — không thể sửa.");

        var scheduleTouched = false;

        if (request.Name is not null)
            entity.Name = RequireName(request.Name);

        if (request.MediaType.HasValue || request.ImageStrategy.HasValue)
        {
            var mediaType = request.MediaType ?? entity.MediaType;
            var imageStrategy = request.ImageStrategy ?? entity.ImageStrategy;
            var normalized = NormalizeMedia(mediaType, imageStrategy);
            if (normalized.mediaType != entity.MediaType
                || normalized.imageStrategy != entity.ImageStrategy)
            {
                scheduleTouched = true;
            }
            entity.MediaType = normalized.mediaType;
            entity.ImageStrategy = normalized.imageStrategy;
        }

        if (request.ScheduleMode.HasValue && request.ScheduleMode.Value != entity.ScheduleMode)
        {
            entity.ScheduleMode = request.ScheduleMode.Value;
            scheduleTouched = true;
        }

        if (request.Weekdays is not null)
        {
            var weekdays = NormalizeWeekdays(entity.ScheduleMode, request.Weekdays);
            var json = Serialize(weekdays);
            if (json != entity.WeekdaysJson)
            {
                entity.WeekdaysJson = json;
                scheduleTouched = true;
            }
        }
        else if (request.ScheduleMode.HasValue)
        {
            // Đổi sang ByWeekday mà không gửi weekdays → validate weekdays hiện có.
            _ = NormalizeWeekdays(entity.ScheduleMode, DeserializeIntList(entity.WeekdaysJson));
        }

        if (request.PublishTimes is not null)
        {
            var times = NormalizePublishTimes(request.PublishTimes);
            var json = Serialize(times);
            if (json != entity.PublishTimesJson)
            {
                entity.PublishTimesJson = json;
                scheduleTouched = true;
            }
        }

        if (request.JitterMinutes.HasValue)
        {
            var jitter = RequireJitter(request.JitterMinutes.Value);
            if (jitter != entity.JitterMinutes)
            {
                entity.JitterMinutes = jitter;
                scheduleTouched = true;
            }
        }

        if (request.StartDate.HasValue)
        {
            var start = NormalizeDate(request.StartDate.Value, "Ngày bắt đầu");
            if (start.Date != entity.StartDate.Date)
            {
                entity.StartDate = start;
                scheduleTouched = true;
            }
        }

        if (request.EndDate.HasValue)
        {
            var end = NormalizeOptionalEnd(request.EndDate, entity.StartDate);
            if (entity.EndDate?.Date != end?.Date)
            {
                entity.EndDate = end;
                scheduleTouched = true;
            }
        }
        else if (request.StartDate.HasValue && entity.EndDate.HasValue)
        {
            // Đổi ngày bắt đầu: vẫn bảo đảm end ≥ start.
            entity.EndDate = NormalizeOptionalEnd(entity.EndDate, entity.StartDate);
        }

        List<Guid>? nextChannels = null;
        List<Guid>? nextGroups = null;

        if (request.ChannelIds is not null)
        {
            nextChannels = NormalizeGuids(request.ChannelIds);
            await EnsureChannelsExistAsync(nextChannels, ct);
        }

        if (request.ChannelGroupIds is not null)
        {
            nextGroups = NormalizeGuids(request.ChannelGroupIds);
            await EnsureGroupsExistAsync(nextGroups, ct);
        }

        if (nextChannels is not null || nextGroups is not null)
        {
            var channels = nextChannels
                ?? await Context.Set<CampaignChannelModel>()
                    .Where(x => !x.IsDeleted && x.CampaignId == id)
                    .Select(x => x.SocialChannelId)
                    .ToListAsync(ct);
            var groups = nextGroups
                ?? await Context.Set<CampaignChannelGroupModel>()
                    .Where(x => !x.IsDeleted && x.CampaignId == id)
                    .Select(x => x.ChannelGroupId)
                    .ToListAsync(ct);
            if (channels.Count == 0 && groups.Count == 0)
                throw new ArgumentException("Cần chọn ít nhất một kênh hoặc nhóm kênh.");

            if (nextChannels is not null)
            {
                var current = await Context.Set<CampaignChannelModel>()
                    .Where(x => !x.IsDeleted && x.CampaignId == id)
                    .Select(x => x.SocialChannelId)
                    .OrderBy(x => x)
                    .ToListAsync(ct);
                if (!current.SequenceEqual(nextChannels.OrderBy(x => x)))
                    scheduleTouched = true;
            }

            if (nextGroups is not null)
            {
                var current = await Context.Set<CampaignChannelGroupModel>()
                    .Where(x => !x.IsDeleted && x.CampaignId == id)
                    .Select(x => x.ChannelGroupId)
                    .OrderBy(x => x)
                    .ToListAsync(ct);
                if (!current.SequenceEqual(nextGroups.OrderBy(x => x)))
                    scheduleTouched = true;
            }
        }

        ApplyUpdateAudit(entity);

        await using var tx = await Context.Database.BeginTransactionAsync(ct);
        try
        {
            if (scheduleTouched)
                await CancelFuturePostsAsync(id, ct);

            if (nextChannels is not null)
                await ReplaceChannelsAsync(id, nextChannels, ct);
            if (nextGroups is not null)
                await ReplaceGroupsAsync(id, nextGroups, ct);

            await Context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        return entity;
    }

    public async Task<CampaignModel?> PauseAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;
        if (entity.Status != CampaignStatus.Running)
            throw new ArgumentException("Chỉ tạm dừng được chiến dịch đang chạy.");

        entity.Status = CampaignStatus.Paused;
        ApplyUpdateAudit(entity);

        await using var tx = await Context.Database.BeginTransactionAsync(ct);
        try
        {
            await CancelFuturePostsAsync(id, ct);
            await Context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        return entity;
    }

    public async Task<CampaignModel?> ResumeAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;
        if (entity.Status == CampaignStatus.Ended)
            throw new ArgumentException("Chiến dịch đã kết thúc — không thể tiếp tục.");
        if (entity.Status != CampaignStatus.Paused)
            throw new ArgumentException("Chỉ tiếp tục được chiến dịch đang tạm dừng.");

        entity.Status = CampaignStatus.Running;
        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<CampaignModel?> EndAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;
        if (entity.Status == CampaignStatus.Ended)
            throw new ArgumentException("Chiến dịch đã kết thúc.");

        entity.Status = CampaignStatus.Ended;
        ApplyUpdateAudit(entity);

        await using var tx = await Context.Database.BeginTransactionAsync(ct);
        try
        {
            await CancelFuturePostsAsync(id, ct);
            await Context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        return entity;
    }

    public override async Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken);
        if (entity is null) return false;

        ApplySoftDeleteAudit(entity);

        await using var tx = await Context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await CancelFuturePostsAsync(id, cancellationToken);

            var now = DateTime.UtcNow;
            var actor = GetCurrentUserName();
            var channels = await Context.Set<CampaignChannelModel>()
                .Where(x => !x.IsDeleted && x.CampaignId == id)
                .ToListAsync(cancellationToken);
            foreach (var row in channels)
            {
                row.IsDeleted = true;
                row.DeletedAt = now;
                row.DeletedBy = actor;
            }

            var groups = await Context.Set<CampaignChannelGroupModel>()
                .Where(x => !x.IsDeleted && x.CampaignId == id)
                .ToListAsync(cancellationToken);
            foreach (var row in groups)
            {
                row.IsDeleted = true;
                row.DeletedAt = now;
                row.DeletedBy = actor;
            }

            await Context.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return true;
    }

    /// <summary>
    /// Huỷ bài chưa đăng, giờ tương lai, thuộc đúng chiến dịch → Cancelled.
    /// Không đụng Publishing/Published/Failed, chiến dịch khác, bài thường.
    /// </summary>
    public async Task<int> CancelFuturePostsAsync(Guid campaignId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var posts = await Context.Set<PostModel>()
            .Where(p => !p.IsDeleted
                && p.CampaignId == campaignId
                && CancellableStatuses.Contains(p.Status)
                && p.ScheduledPublishAt != null
                && p.ScheduledPublishAt > now)
            .ToListAsync(ct);

        var actor = GetCurrentUserName();
        foreach (var post in posts)
        {
            post.Status = PostStatus.Cancelled;
            post.UpdatedAt = now;
            post.UpdatedBy = actor;
        }

        return posts.Count;
    }

    private async Task ReplaceChannelsAsync(Guid campaignId, List<Guid> channelIds, CancellationToken ct)
    {
        var existing = await Context.Set<CampaignChannelModel>()
            .Where(x => x.CampaignId == campaignId)
            .ToListAsync(ct);
        UpsertMembership(
            existing,
            channelIds,
            id => existing.FirstOrDefault(x => x.SocialChannelId == id),
            id => new CampaignChannelModel
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                SocialChannelId = id,
            },
            row => row.SocialChannelId);
    }

    private async Task ReplaceGroupsAsync(Guid campaignId, List<Guid> groupIds, CancellationToken ct)
    {
        var existing = await Context.Set<CampaignChannelGroupModel>()
            .Where(x => x.CampaignId == campaignId)
            .ToListAsync(ct);
        UpsertMembership(
            existing,
            groupIds,
            id => existing.FirstOrDefault(x => x.ChannelGroupId == id),
            id => new CampaignChannelGroupModel
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                ChannelGroupId = id,
            },
            row => row.ChannelGroupId);
        await Task.CompletedTask;
    }

    private void UpsertMembership<T>(
        List<T> existing,
        List<Guid> keepIds,
        Func<Guid, T?> find,
        Func<Guid, T> create,
        Func<T, Guid> getKey)
        where T : BaseEntity
    {
        var keep = keepIds.ToHashSet();
        var now = DateTime.UtcNow;
        var actor = GetCurrentUserName();

        foreach (var row in existing.Where(x => !x.IsDeleted && !keep.Contains(getKey(x))))
        {
            row.IsDeleted = true;
            row.DeletedAt = now;
            row.DeletedBy = actor;
        }

        foreach (var id in keepIds)
        {
            var row = find(id);
            if (row is null)
            {
                var created = create(id);
                created.CreatedAt = now;
                created.CreatedBy = actor;
                created.IsDeleted = false;
                Context.Set<T>().Add(created);
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
    }

    private async Task EnsureChannelsExistAsync(List<Guid> channelIds, CancellationToken ct)
    {
        if (channelIds.Count == 0) return;
        var found = await Context.Set<SocialChannelModel>()
            .Where(c => !c.IsDeleted && channelIds.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (channelIds.Except(found).Any())
            throw new ArgumentException("Có kênh không tồn tại hoặc đã bị xoá.");
    }

    private async Task EnsureGroupsExistAsync(List<Guid> groupIds, CancellationToken ct)
    {
        if (groupIds.Count == 0) return;
        var found = await Context.Set<ChannelGroupModel>()
            .Where(g => !g.IsDeleted && groupIds.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync(ct);
        if (groupIds.Except(found).Any())
            throw new ArgumentException("Có nhóm kênh không tồn tại hoặc đã bị xoá.");
    }

    private async Task<List<CampaignResponse>> MapResponsesAsync(
        List<CampaignModel> campaigns, CancellationToken ct)
    {
        if (campaigns.Count == 0) return [];

        var ids = campaigns.Select(c => c.Id).ToList();
        var channelRows = await Context.Set<CampaignChannelModel>()
            .Where(x => !x.IsDeleted && ids.Contains(x.CampaignId))
            .Select(x => new { x.CampaignId, x.SocialChannelId })
            .ToListAsync(ct);
        var groupRows = await Context.Set<CampaignChannelGroupModel>()
            .Where(x => !x.IsDeleted && ids.Contains(x.CampaignId))
            .Select(x => new { x.CampaignId, x.ChannelGroupId })
            .ToListAsync(ct);

        return campaigns.Select(c => new CampaignResponse
        {
            Id = c.Id,
            Name = c.Name,
            MediaType = c.MediaType,
            ImageStrategy = c.ImageStrategy,
            ChannelIds = channelRows
                .Where(x => x.CampaignId == c.Id)
                .Select(x => x.SocialChannelId)
                .Distinct()
                .ToList(),
            ChannelGroupIds = groupRows
                .Where(x => x.CampaignId == c.Id)
                .Select(x => x.ChannelGroupId)
                .Distinct()
                .ToList(),
            ScheduleMode = c.ScheduleMode,
            Weekdays = DeserializeIntList(c.WeekdaysJson),
            PublishTimes = DeserializeStringList(c.PublishTimesJson),
            JitterMinutes = c.JitterMinutes,
            StartDate = c.StartDate,
            EndDate = c.EndDate,
            Status = c.Status,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
        }).ToList();
    }

    private static (CampaignMediaType mediaType, CampaignImageStrategy? imageStrategy) NormalizeMedia(
        CampaignMediaType mediaType, CampaignImageStrategy? imageStrategy)
    {
        if (mediaType == CampaignMediaType.Video)
            return (mediaType, null);

        if (mediaType != CampaignMediaType.Image)
            throw new ArgumentException("Loại media không hợp lệ.");

        var strategy = imageStrategy ?? CampaignImageStrategy.KeepOld;
        if (!Enum.IsDefined(strategy))
            throw new ArgumentException("Chiến lược hình ảnh không hợp lệ.");
        return (mediaType, strategy);
    }

    private static List<int> NormalizeWeekdays(CampaignScheduleMode mode, IEnumerable<int>? weekdays)
    {
        if (mode == CampaignScheduleMode.AllWeek)
            return [];

        if (mode != CampaignScheduleMode.ByWeekday)
            throw new ArgumentException("Chế độ lịch không hợp lệ.");

        var list = (weekdays ?? [])
            .Where(d => d is >= 1 and <= 7)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        if (list.Count == 0)
            throw new ArgumentException("Chế độ Theo thứ cần chọn ít nhất một thứ.");
        return list;
    }

    private static List<string> NormalizePublishTimes(IEnumerable<string>? times)
    {
        var list = (times ?? [])
            .Select(t => (t ?? string.Empty).Trim())
            .Where(t => t.Length > 0)
            .ToList();
        if (list.Count == 0)
            throw new ArgumentException("Cần ít nhất một giờ đăng.");

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in list)
        {
            if (!TimeRegex.IsMatch(raw))
                throw new ArgumentException($"Giờ đăng '{raw}' sai định dạng (cần HH:mm).");
            if (!seen.Add(raw))
                throw new ArgumentException($"Giờ đăng '{raw}' bị trùng.");
            normalized.Add(raw);
        }

        return normalized.OrderBy(t => t, StringComparer.Ordinal).ToList();
    }

    private static int RequireJitter(int jitter)
    {
        if (jitter is < 0 or > 240)
            throw new ArgumentException("Lệch phút phải trong khoảng 0–240.");
        return jitter;
    }

    private static DateTime NormalizeDate(DateTime value, string label)
    {
        // Chuẩn hoá về UTC midnight của phần ngày (client gửi date).
        var date = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Date, DateTimeKind.Utc)
            : value.ToUniversalTime().Date;
        if (date == default)
            throw new ArgumentException($"{label} không hợp lệ.");
        return DateTime.SpecifyKind(date, DateTimeKind.Utc);
    }

    private static DateTime? NormalizeOptionalEnd(DateTime? end, DateTime start)
    {
        if (!end.HasValue) return null;
        var normalized = NormalizeDate(end.Value, "Ngày kết thúc");
        if (normalized.Date < start.Date)
            throw new ArgumentException("Ngày kết thúc phải ≥ ngày bắt đầu.");
        return normalized;
    }

    private static string RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tên chiến dịch không được để trống");
        return name.Trim();
    }

    private static List<Guid> NormalizeGuids(IEnumerable<Guid>? ids)
        => (ids ?? []).Where(x => x != Guid.Empty).Distinct().ToList();

    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, JsonOptions);

    private static List<int> DeserializeIntList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
