using System.Text.Json;
using Backend.Data;
using Backend.Modules.Crm.Customers;
using Backend.Modules.PageMessage;
using Backend.Modules.SocialChannel.Enums;
using Backend.Modules.SocialComment;
using Backend.Modules.Users;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Crm.Opportunities;

public class CrmOpportunityService(
    AppDbContext db,
    IUserContext userContext,
    CrmCustomerService customers)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<PagedResult<CrmOpportunityListItemResponse>> FilterAsync(
        CrmOpportunityFilterRequest request, CancellationToken ct = default)
    {
        var index = Math.Max(1, request.Index);
        var size = Math.Clamp(request.Size <= 0 ? 20 : request.Size, 1, 100);
        var query = await BuildFilteredQueryAsync(request, ct);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.LastActivityAtUtc ?? x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((index - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new PagedResult<CrmOpportunityListItemResponse>
        {
            Items = await HydrateListAsync(rows, ct),
            Total = total,
            Index = index,
            Size = size
        };
    }

    /// <summary>Cơ hội phải tồn tại, chưa xoá và cùng khách; null = không gắn. Sai → 400.</summary>
    public static async Task EnsureLinkableAsync(
        AppDbContext db, Guid? opportunityId, Guid customerId, CancellationToken ct = default)
    {
        if (opportunityId is null) return;
        var ok = await db.CrmOpportunities.AsNoTracking().AnyAsync(
            x => x.Id == opportunityId && !x.IsDeleted && x.CrmCustomerId == customerId, ct);
        if (!ok)
            throw new InvalidOperationException("Cơ hội không tồn tại hoặc không thuộc khách này");
    }

    /// <summary>Cập nhật LastActivityAtUtc của cơ hội (lưu cùng SaveChanges của caller).</summary>
    public static async Task TouchActivityAsync(
        AppDbContext db, Guid? opportunityId, CancellationToken ct = default)
    {
        if (opportunityId is null) return;
        var opp = await db.CrmOpportunities.FirstOrDefaultAsync(x => x.Id == opportunityId && !x.IsDeleted, ct);
        if (opp is not null) opp.LastActivityAtUtc = DateTime.UtcNow;
    }

    public async Task<CrmOpportunityStatsResponse> StatsAsync(
        CrmOpportunityFilterRequest request, CancellationToken ct = default)
    {
        // Stats ignore pagination but honor filters. Default archived=false unless specified.
        var filter = CloneFilter(request);
        var query = await BuildFilteredQueryAsync(filter, ct, includeArchivedExplicit: true);
        var rows = await query
            .Select(x => new { x.Id, x.Status, x.IsArchived, x.ExpectedValue })
            .ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToList();
        // Hoạt động = nhắc việc + ghi chú (chưa xoá) gắn vào các cơ hội đang được thống kê.
        var activity = await db.CrmCustomerReminders.CountAsync(
                           x => !x.IsDeleted && x.CrmOpportunityId != null && ids.Contains(x.CrmOpportunityId.Value), ct)
                       + await db.CrmCustomerNotes.CountAsync(
                           x => !x.IsDeleted && x.CrmOpportunityId != null && ids.Contains(x.CrmOpportunityId.Value), ct);

        return new CrmOpportunityStatsResponse
        {
            Total = rows.Count,
            Open = rows.Count(x => x.Status == CrmOpportunityStatus.Open && !x.IsArchived),
            Won = rows.Count(x => x.Status == CrmOpportunityStatus.Won && !x.IsArchived),
            Lost = rows.Count(x => x.Status == CrmOpportunityStatus.Lost && !x.IsArchived),
            Activity = activity,
            Rev = rows.Where(x => x.Status == CrmOpportunityStatus.Won && !x.IsArchived)
                .Sum(x => x.ExpectedValue)
        };
    }

    public async Task<CrmOpportunityPipelineResponse> PipelineAsync(
        CrmOpportunityPipelineRequest request, CancellationToken ct = default)
    {
        var perStage = Math.Clamp(request.PerStage <= 0 ? 20 : request.PerStage, 1, 100);
        var filters = request.Filters ?? new CrmOpportunityFilterRequest();
        var stages = await db.CrmOpportunityStages.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        var columns = new List<CrmOpportunityPipelineStageColumn>();
        foreach (var stage in stages)
        {
            var stageFilter = CloneFilter(filters);
            stageFilter.StageId = stage.Id;
            var query = await BuildFilteredQueryAsync(stageFilter, ct: ct);
            var total = await query.CountAsync(ct);
            var rows = await query
                .OrderByDescending(x => x.LastActivityAtUtc ?? x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .Take(perStage)
                .ToListAsync(ct);

            columns.Add(new CrmOpportunityPipelineStageColumn
            {
                StageId = stage.Id,
                StageName = stage.Name,
                StageColor = stage.Color,
                Kind = stage.Kind,
                SortOrder = stage.SortOrder,
                Total = total,
                Items = await HydrateListAsync(rows, ct)
            });
        }

        return new CrmOpportunityPipelineResponse { Columns = columns };
    }

    public async Task<PagedResult<CrmOpportunityListItemResponse>> PipelineStagePageAsync(
        Guid stageId, CrmOpportunityPipelinePageRequest request, CancellationToken ct = default)
    {
        var stage = await db.CrmOpportunityStages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == stageId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Giai đoạn không tồn tại");

        var index = Math.Max(1, request.Index);
        var size = Math.Clamp(request.Size <= 0 ? 20 : request.Size, 1, 100);
        var filters = CloneFilter(request.Filters ?? new CrmOpportunityFilterRequest());
        filters.StageId = stage.Id;
        var query = await BuildFilteredQueryAsync(filters, ct: ct);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.LastActivityAtUtc ?? x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((index - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new PagedResult<CrmOpportunityListItemResponse>
        {
            Items = await HydrateListAsync(rows, ct),
            Total = total,
            Index = index,
            Size = size
        };
    }

    public async Task<CrmOpportunityDetailResponse?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.CrmOpportunities.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (entity is null) return null;
        var list = await HydrateListAsync([entity], ct);
        var item = list[0];
        return new CrmOpportunityDetailResponse
        {
            Id = item.Id,
            Title = item.Title,
            CrmCustomerId = item.CrmCustomerId,
            CustomerName = item.CustomerName,
            CustomerPhoneE164 = item.CustomerPhoneE164,
            StageId = item.StageId,
            StageName = item.StageName,
            StageColor = item.StageColor,
            StageKind = item.StageKind,
            Status = item.Status,
            IsArchived = item.IsArchived,
            AssigneeUserId = item.AssigneeUserId,
            AssignedTo = item.AssignedTo,
            WatcherUserIds = item.WatcherUserIds,
            ExpectedValue = item.ExpectedValue,
            Source = item.Source,
            SocialChannelId = item.SocialChannelId,
            ChannelName = item.ChannelName,
            ChannelPlatform = item.ChannelPlatform,
            PageConversationId = item.PageConversationId,
            SocialCommentId = item.SocialCommentId,
            LastActivityAtUtc = item.LastActivityAtUtc,
            SourceSnippet = item.SourceSnippet,
            CreatedAt = item.CreatedAt,
            LostReason = entity.LostReason,
            ClosedAtUtc = entity.ClosedAtUtc,
            UpdatedAt = entity.UpdatedAt
        };
    }

    public async Task<CrmOpportunityDetailResponse> CreateAsync(
        CreateCrmOpportunityRequest request, CancellationToken ct = default)
    {
        var customer = await db.CrmCustomers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.CrmCustomerId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Khách hàng không tồn tại");

        if (request.ExpectedValue < 0)
            throw new InvalidOperationException("ExpectedValue phải >= 0");

        var title = (request.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Tiêu đề cơ hội bắt buộc");

        var stageId = request.StageId ?? CrmOpportunityStageIds.Moi;
        var stage = await RequireStageAsync(stageId, ct);
        await EnsureOpenUniqueAsync(request.PageConversationId, request.SocialCommentId, excludeId: null, ct);

        var now = DateTime.UtcNow;
        var entity = new CrmOpportunityModel
        {
            Id = Guid.NewGuid(),
            CrmCustomerId = customer.Id,
            Title = title,
            StageId = stage.Id,
            Status = StatusFromKind(stage.Kind),
            IsArchived = false,
            AssigneeUserId = request.AssigneeUserId,
            AssignedTo = await ResolveAssigneeNameAsync(request.AssigneeUserId, ct),
            Source = request.Source,
            SocialChannelId = request.SocialChannelId,
            PageConversationId = request.PageConversationId,
            SocialCommentId = request.SocialCommentId,
            ExpectedValue = request.ExpectedValue,
            ClosedAtUtc = stage.Kind is CrmOpportunityStageKind.Won or CrmOpportunityStageKind.Lost
                ? now
                : null,
            LastActivityAtUtc = now,
            CreatedAt = now,
            CreatedBy = userContext.GetCurrentUserName()
        };
        db.CrmOpportunities.Add(entity);

        foreach (var uid in (request.WatcherUserIds ?? []).Distinct())
        {
            db.CrmOpportunityWatchers.Add(new CrmOpportunityWatcherModel
            {
                Id = Guid.NewGuid(),
                OpportunityId = entity.Id,
                UserId = uid,
                CreatedAt = now,
                CreatedBy = userContext.GetCurrentUserName()
            });
        }

        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(customer.Id, "OppCreate",
            JsonSerializer.Serialize(new { entity.Id, entity.Title }, JsonOpts), ct);
        return (await GetAsync(entity.Id, ct))!;
    }

    public async Task<CrmOpportunityDetailResponse?> UpdateAsync(
        Guid id, UpdateCrmOpportunityRequest request, CancellationToken ct = default)
    {
        var entity = await db.CrmOpportunities
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (entity is null) return null;

        if (request.ExpectedValue < 0)
            throw new InvalidOperationException("ExpectedValue phải >= 0");

        var title = (request.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Tiêu đề cơ hội bắt buộc");

        entity.Title = title;
        entity.ExpectedValue = request.ExpectedValue;
        entity.AssigneeUserId = request.AssigneeUserId;
        entity.AssignedTo = await ResolveAssigneeNameAsync(request.AssigneeUserId, ct);
        if (request.LostReason is not null)
            entity.LostReason = string.IsNullOrWhiteSpace(request.LostReason) ? null : request.LostReason.Trim();
        entity.LastActivityAtUtc = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(entity.CrmCustomerId, "OppUpdate",
            JsonSerializer.Serialize(new { entity.Id }, JsonOpts), ct);
        return await GetAsync(entity.Id, ct);
    }

    public async Task<CrmOpportunityDetailResponse> MoveStageAsync(
        Guid id, MoveCrmOpportunityStageRequest request, CancellationToken ct = default)
    {
        var entity = await db.CrmOpportunities
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Cơ hội không tồn tại");

        var stage = await RequireStageAsync(request.StageId, ct);
        if (stage.Kind == CrmOpportunityStageKind.Lost
            && string.IsNullOrWhiteSpace(request.LostReason))
            throw new InvalidOperationException("LostReason bắt buộc khi chuyển sang giai đoạn Thất bại");

        // Quay về giai đoạn Open không được tạo ra 2 cơ hội Open cho cùng hội thoại/thread.
        if (stage.Kind == CrmOpportunityStageKind.Open && !entity.IsArchived)
            await EnsureOpenUniqueAsync(entity.PageConversationId, entity.SocialCommentId, entity.Id, ct);

        var now = DateTime.UtcNow;
        entity.StageId = stage.Id;
        entity.Status = StatusFromKind(stage.Kind);
        entity.LastActivityAtUtc = now;
        entity.UpdatedAt = now;
        entity.UpdatedBy = userContext.GetCurrentUserName();

        if (stage.Kind == CrmOpportunityStageKind.Won)
        {
            entity.ClosedAtUtc = now;
            entity.LostReason = null;
        }
        else if (stage.Kind == CrmOpportunityStageKind.Lost)
        {
            entity.ClosedAtUtc = now;
            entity.LostReason = request.LostReason!.Trim();
        }
        else
        {
            entity.ClosedAtUtc = null;
            entity.LostReason = null;
        }

        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(entity.CrmCustomerId, "OppMoveStage",
            JsonSerializer.Serialize(new { opportunityId = entity.Id, stageId = stage.Id, kind = stage.Kind }, JsonOpts), ct);
        return (await GetAsync(entity.Id, ct))!;
    }

    public async Task<CrmOpportunityDetailResponse> AssignAsync(
        Guid id, AssignCrmOpportunityRequest request, CancellationToken ct = default)
    {
        var entity = await db.CrmOpportunities
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Cơ hội không tồn tại");

        entity.AssigneeUserId = request.AssigneeUserId;
        entity.AssignedTo = string.IsNullOrWhiteSpace(request.AssignedTo)
            ? await ResolveAssigneeNameAsync(request.AssigneeUserId, ct)
            : request.AssignedTo.Trim();
        entity.LastActivityAtUtc = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(entity.CrmCustomerId, "OppAssign",
            JsonSerializer.Serialize(new { entity.Id, entity.AssigneeUserId }, JsonOpts), ct);
        return (await GetAsync(entity.Id, ct))!;
    }

    public async Task AddWatcherAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var entity = await db.CrmOpportunities
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Cơ hội không tồn tại");

        var existing = await db.CrmOpportunityWatchers
            .FirstOrDefaultAsync(x => x.OpportunityId == id && x.UserId == userId && !x.IsDeleted, ct);
        if (existing is null)
        {
            db.CrmOpportunityWatchers.Add(new CrmOpportunityWatcherModel
            {
                Id = Guid.NewGuid(),
                OpportunityId = id,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userContext.GetCurrentUserName()
            });
            entity.LastActivityAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await customers.AddLogAsync(entity.CrmCustomerId, "OppAddWatcher",
                JsonSerializer.Serialize(new { id, userId }, JsonOpts), ct);
        }
    }

    public async Task RemoveWatcherAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        var entity = await db.CrmOpportunities
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Cơ hội không tồn tại");

        var watcher = await db.CrmOpportunityWatchers
            .FirstOrDefaultAsync(x => x.OpportunityId == id && x.UserId == userId && !x.IsDeleted, ct);
        if (watcher is null) return;

        watcher.IsDeleted = true;
        watcher.DeletedAt = DateTime.UtcNow;
        watcher.DeletedBy = userContext.GetCurrentUserName();
        entity.LastActivityAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(entity.CrmCustomerId, "OppRemoveWatcher",
            JsonSerializer.Serialize(new { id, userId }, JsonOpts), ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.CrmOpportunities
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Cơ hội không tồn tại");
        entity.IsArchived = true;
        entity.LastActivityAtUtc = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(entity.CrmCustomerId, "OppArchive",
            JsonSerializer.Serialize(new { id }, JsonOpts), ct);
    }

    public async Task UnarchiveAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.CrmOpportunities
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Cơ hội không tồn tại");
        if (entity.Status == CrmOpportunityStatus.Open)
            await EnsureOpenUniqueAsync(entity.PageConversationId, entity.SocialCommentId, entity.Id, ct);
        entity.IsArchived = false;
        entity.LastActivityAtUtc = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(entity.CrmCustomerId, "OppUnarchive",
            JsonSerializer.Serialize(new { id }, JsonOpts), ct);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.CrmOpportunities
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Cơ hội không tồn tại");
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(entity.CrmCustomerId, "OppSoftDelete",
            JsonSerializer.Serialize(new { id }, JsonOpts), ct);
    }

    public async Task<CrmOpportunityDetailResponse> FromConversationAsync(
        CrmOpportunityFromConversationRequest request, CancellationToken ct = default)
    {
        if (!TryParseKind(request.Kind, out var kind))
            throw new InvalidOperationException("Kind phải là message hoặc comment");

        SocialPlatform platform;
        Guid socialChannelId;
        string externalId;
        string? displayName;
        string? avatarUrl;
        Guid? pageConversationId = null;
        Guid? socialCommentId = null;
        CrmOpportunitySource source;
        string? channelName = null;
        Guid? conversationAssigneeId;

        if (kind == "message")
        {
            var conv = await db.PageConversations
                .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, ct)
                ?? throw new KeyNotFoundException("Hội thoại không tồn tại");
            var channel = await db.SocialChannels
                .FirstOrDefaultAsync(x => x.Id == conv.SocialChannelId, ct)
                ?? throw new KeyNotFoundException("Kênh không tồn tại");
            if (string.IsNullOrWhiteSpace(conv.ParticipantExternalId))
                throw new InvalidOperationException("Hội thoại thiếu ParticipantExternalId");

            platform = channel.Platform;
            socialChannelId = channel.Id;
            externalId = conv.ParticipantExternalId.Trim();
            displayName = conv.ParticipantName;
            avatarUrl = conv.ParticipantAvatarUrl;
            pageConversationId = conv.Id;
            conversationAssigneeId = conv.AssignedUserId;
            source = CrmOpportunitySource.Message;
            channelName = channel.PageName;

            var existing = await db.CrmOpportunities.AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    !x.IsDeleted && !x.IsArchived
                    && x.Status == CrmOpportunityStatus.Open
                    && x.PageConversationId == conv.Id, ct);
            if (existing is not null)
                return (await GetAsync(existing.Id, ct))!;
        }
        else
        {
            var comment = await db.SocialComments
                .FirstOrDefaultAsync(x => x.Id == request.Id && !x.IsDeleted, ct)
                ?? throw new KeyNotFoundException("Bình luận không tồn tại");
            var root = comment;
            while (root.ParentCommentId.HasValue)
            {
                var parent = await db.SocialComments
                    .FirstOrDefaultAsync(x => x.Id == root.ParentCommentId.Value && !x.IsDeleted, ct);
                if (parent is null) break;
                root = parent;
            }

            var channel = await db.SocialChannels
                .FirstOrDefaultAsync(x => x.Id == root.SocialChannelId, ct)
                ?? throw new KeyNotFoundException("Kênh không tồn tại");
            if (string.IsNullOrWhiteSpace(root.AuthorExternalId))
                throw new InvalidOperationException("Bình luận thiếu AuthorExternalId");

            platform = channel.Platform;
            socialChannelId = channel.Id;
            externalId = root.AuthorExternalId.Trim();
            displayName = root.AuthorName ?? root.AuthorUsername;
            avatarUrl = null;
            socialCommentId = root.Id;
            conversationAssigneeId = root.AssignedUserId ?? comment.AssignedUserId;
            source = CrmOpportunitySource.Comment;
            channelName = channel.PageName;

            var existing = await db.CrmOpportunities.AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    !x.IsDeleted && !x.IsArchived
                    && x.Status == CrmOpportunityStatus.Open
                    && x.SocialCommentId == root.Id, ct);
            if (existing is not null)
                return (await GetAsync(existing.Id, ct))!;
        }

        // Khớp chính xác identity — EnsureLinked khi chưa có (hành động ghi chủ động).
        var customerId = await customers.EnsureLinkedAsync(
            platform,
            socialChannelId,
            externalId,
            displayName,
            kind == "message" ? CrmIdentitySource.Message : CrmIdentitySource.Comment,
            avatarUrl,
            ct);

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"Cơ hội — {displayName ?? externalId}"
            : request.Title.Trim();

        try
        {
            return await CreateAsync(new CreateCrmOpportunityRequest
            {
                CrmCustomerId = customerId,
                Title = title,
                StageId = CrmOpportunityStageIds.Moi,
                // Người phụ trách hội thoại; chưa có thì người đang tạo (để hiện ở "Việc của tôi").
                AssigneeUserId = conversationAssigneeId ?? userContext.GetCurrentUserId(),
                Source = source,
                SocialChannelId = socialChannelId,
                PageConversationId = pageConversationId,
                SocialCommentId = socialCommentId
            }, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            // Bấm 2 lần / request song song: unique index chặn dòng thứ hai → trả cơ hội đang mở.
            db.ChangeTracker.Clear();
            var existing = await GetByConversationAsync(kind, pageConversationId ?? socialCommentId!.Value, ct);
            if (existing is not null) return existing;
            throw;
        }
    }

    public async Task<CrmOpportunityDetailResponse?> GetByConversationAsync(
        string kind, Guid id, CancellationToken ct = default)
    {
        if (!TryParseKind(kind, out var parsed))
            return null;

        CrmOpportunityModel? existing = null;
        if (parsed == "message")
        {
            existing = await db.CrmOpportunities.AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    !x.IsDeleted && !x.IsArchived
                    && x.Status == CrmOpportunityStatus.Open
                    && x.PageConversationId == id, ct);
        }
        else
        {
            existing = await db.CrmOpportunities.AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    !x.IsDeleted && !x.IsArchived
                    && x.Status == CrmOpportunityStatus.Open
                    && x.SocialCommentId == id, ct);
        }

        return existing is null ? null : await GetAsync(existing.Id, ct);
    }

    private async Task EnsureOpenUniqueAsync(
        Guid? pageConversationId, Guid? socialCommentId, Guid? excludeId, CancellationToken ct)
    {
        if (pageConversationId.HasValue)
        {
            var dup = await db.CrmOpportunities.AnyAsync(x =>
                !x.IsDeleted && !x.IsArchived
                && x.Status == CrmOpportunityStatus.Open
                && x.PageConversationId == pageConversationId
                && (excludeId == null || x.Id != excludeId), ct);
            if (dup)
                throw new InvalidOperationException("Hội thoại đã có cơ hội đang mở");
        }

        if (socialCommentId.HasValue)
        {
            var dup = await db.CrmOpportunities.AnyAsync(x =>
                !x.IsDeleted && !x.IsArchived
                && x.Status == CrmOpportunityStatus.Open
                && x.SocialCommentId == socialCommentId
                && (excludeId == null || x.Id != excludeId), ct);
            if (dup)
                throw new InvalidOperationException("Luồng bình luận đã có cơ hội đang mở");
        }
    }

    private async Task<IQueryable<CrmOpportunityModel>> BuildFilteredQueryAsync(
        CrmOpportunityFilterRequest request,
        CancellationToken ct,
        bool includeArchivedExplicit = false)
    {
        IQueryable<CrmOpportunityModel> query = db.CrmOpportunities.AsNoTracking()
            .Where(x => !x.IsDeleted);

        if (request.StageId.HasValue)
            query = query.Where(x => x.StageId == request.StageId);
        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status);
        if (request.IsArchived.HasValue)
            query = query.Where(x => x.IsArchived == request.IsArchived.Value);
        else if (!includeArchivedExplicit)
            query = query.Where(x => !x.IsArchived);

        if (request.SocialChannelId.HasValue)
            query = query.Where(x => x.SocialChannelId == request.SocialChannelId);
        if (request.Source.HasValue)
            query = query.Where(x => x.Source == request.Source);
        if (request.CreatedFrom.HasValue)
            query = query.Where(x => x.CreatedAt >= request.CreatedFrom);
        if (request.CreatedTo.HasValue)
            query = query.Where(x => x.CreatedAt <= request.CreatedTo);

        // Assignee filters
        var mineId = userContext.GetCurrentUserId();
        if (request.AssignedMine == true
            || string.Equals(request.AssigneeFilter, "mine", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.AssigneeUserId == mineId);
        }
        else if (request.UnassignedOnly == true
                 || string.Equals(request.AssigneeFilter, "unassigned", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.AssigneeUserId == null);
        }
        else if (request.AssigneeUserId.HasValue)
        {
            query = query.Where(x => x.AssigneeUserId == request.AssigneeUserId);
        }
        else if (!string.IsNullOrWhiteSpace(request.AssigneeFilter)
                 && Guid.TryParse(request.AssigneeFilter, out var assigneeId))
        {
            query = query.Where(x => x.AssigneeUserId == assigneeId);
        }

        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : request.Keyword.Trim();
        if (keyword is not null)
        {
            var customerIds = await db.CrmCustomers.AsNoTracking()
                .Where(c => !c.IsDeleted
                            && (c.DisplayName.Contains(keyword)
                                || (c.PhoneE164 != null && c.PhoneE164.Contains(keyword))))
                .Select(c => c.Id)
                .ToListAsync(ct);
            query = query.Where(x =>
                x.Title.Contains(keyword) || customerIds.Contains(x.CrmCustomerId));
        }

        return query;
    }

    private async Task<List<CrmOpportunityListItemResponse>> HydrateListAsync(
        List<CrmOpportunityModel> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];

        var oppIds = rows.Select(x => x.Id).ToList();
        var customerIds = rows.Select(x => x.CrmCustomerId).Distinct().ToList();
        var stageIds = rows.Select(x => x.StageId).Distinct().ToList();
        var channelIds = rows.Where(x => x.SocialChannelId.HasValue)
            .Select(x => x.SocialChannelId!.Value).Distinct().ToList();
        var convIds = rows.Where(x => x.PageConversationId.HasValue)
            .Select(x => x.PageConversationId!.Value).Distinct().ToList();

        var customersMap = await db.CrmCustomers.AsNoTracking()
            .Where(x => customerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var stages = await db.CrmOpportunityStages.AsNoTracking()
            .Where(x => stageIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        var channels = channelIds.Count == 0
            ? new Dictionary<Guid, (string Name, SocialPlatform Platform)>()
            : await db.SocialChannels.AsNoTracking()
                .Where(x => channelIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => (Name: x.PageName, Platform: x.Platform), ct);
        // Tên người phụ trách lấy theo AssigneeUserId lúc đọc: bản ghi cũ từng lưu id dạng hex
        // trong AssignedTo vẫn hiện đúng tên, và đổi tên người dùng cũng được cập nhật.
        var assigneeIds = rows.Where(x => x.AssigneeUserId.HasValue)
            .Select(x => x.AssigneeUserId!.Value).Distinct().ToList();
        var assigneeNames = assigneeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await db.Users.AsNoTracking()
                .Where(u => assigneeIds.Contains(u.Id))
                .ToListAsync(ct))
                .ToDictionary(u => u.Id, UsersService.ResolveDisplayName);
        var watchers = await db.CrmOpportunityWatchers.AsNoTracking()
            .Where(x => !x.IsDeleted && oppIds.Contains(x.OpportunityId))
            .GroupBy(x => x.OpportunityId)
            .ToDictionaryAsync(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(w => w.UserId).ToList(), ct);

        var snippets = new Dictionary<Guid, string?>();
        if (convIds.Count > 0)
        {
            var messages = await db.PageMessages.AsNoTracking()
                .Where(x => !x.IsDeleted && convIds.Contains(x.PageConversationId))
                .Select(x => new { x.PageConversationId, x.Text, x.SentAt, x.CreatedAt })
                .ToListAsync(ct);
            foreach (var group in messages.GroupBy(x => x.PageConversationId))
            {
                var last = group.OrderByDescending(x => x.SentAt ?? x.CreatedAt).FirstOrDefault();
                snippets[group.Key] = last?.Text;
            }

            var convSnippets = await db.PageConversations.AsNoTracking()
                .Where(x => convIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Snippet })
                .ToListAsync(ct);
            foreach (var c in convSnippets)
            {
                if (!snippets.ContainsKey(c.Id) || string.IsNullOrWhiteSpace(snippets[c.Id]))
                    snippets[c.Id] = c.Snippet;
            }
        }

        var result = new List<CrmOpportunityListItemResponse>(rows.Count);
        foreach (var row in rows)
        {
            customersMap.TryGetValue(row.CrmCustomerId, out var customer);
            stages.TryGetValue(row.StageId, out var stage);
            string? channelName = null;
            SocialPlatform? platform = null;
            if (row.SocialChannelId.HasValue && channels.TryGetValue(row.SocialChannelId.Value, out var ch))
            {
                channelName = ch.Name;
                platform = ch.Platform;
            }

            result.Add(new CrmOpportunityListItemResponse
            {
                Id = row.Id,
                Title = row.Title,
                CrmCustomerId = row.CrmCustomerId,
                CustomerName = customer?.DisplayName,
                CustomerPhoneE164 = customer?.PhoneE164,
                StageId = row.StageId,
                StageName = stage?.Name,
                StageColor = stage?.Color,
                StageKind = stage?.Kind ?? CrmOpportunityStageKind.Open,
                Status = row.Status,
                IsArchived = row.IsArchived,
                AssigneeUserId = row.AssigneeUserId,
                AssignedTo = row.AssigneeUserId.HasValue
                             && assigneeNames.TryGetValue(row.AssigneeUserId.Value, out var assigneeName)
                    ? assigneeName
                    : row.AssignedTo,
                WatcherUserIds = watchers.GetValueOrDefault(row.Id) ?? [],
                ExpectedValue = row.ExpectedValue,
                Source = row.Source,
                SocialChannelId = row.SocialChannelId,
                ChannelName = channelName,
                ChannelPlatform = platform,
                PageConversationId = row.PageConversationId,
                SocialCommentId = row.SocialCommentId,
                LastActivityAtUtc = row.LastActivityAtUtc,
                SourceSnippet = row.PageConversationId.HasValue
                    ? snippets.GetValueOrDefault(row.PageConversationId.Value)
                    : null,
                CreatedAt = row.CreatedAt
            });
        }

        return result;
    }

    /// <summary>Tên hiển thị người phụ trách (giống inbox: UsersService.ResolveDisplayName).</summary>
    private async Task<string?> ResolveAssigneeNameAsync(Guid? userId, CancellationToken ct)
    {
        if (userId is null) return null;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        return user is null ? userId.Value.ToString("N") : UsersService.ResolveDisplayName(user);
    }

    private async Task<CrmOpportunityStageModel> RequireStageAsync(Guid stageId, CancellationToken ct)
        => await db.CrmOpportunityStages.AsNoTracking()
               .FirstOrDefaultAsync(x => x.Id == stageId && !x.IsDeleted, ct)
           ?? throw new KeyNotFoundException("Giai đoạn không tồn tại");

    private static CrmOpportunityStatus StatusFromKind(CrmOpportunityStageKind kind) => kind switch
    {
        CrmOpportunityStageKind.Won => CrmOpportunityStatus.Won,
        CrmOpportunityStageKind.Lost => CrmOpportunityStatus.Lost,
        _ => CrmOpportunityStatus.Open
    };

    private static bool TryParseKind(string? kind, out string parsed)
    {
        if (string.Equals(kind, "message", StringComparison.OrdinalIgnoreCase))
        {
            parsed = "message";
            return true;
        }

        if (string.Equals(kind, "comment", StringComparison.OrdinalIgnoreCase))
        {
            parsed = "comment";
            return true;
        }

        parsed = "";
        return false;
    }

    private static CrmOpportunityFilterRequest CloneFilter(CrmOpportunityFilterRequest src) => new()
    {
        Index = src.Index,
        Size = src.Size,
        Keyword = src.Keyword,
        StageId = src.StageId,
        Status = src.Status,
        IsArchived = src.IsArchived,
        AssigneeFilter = src.AssigneeFilter,
        AssigneeUserId = src.AssigneeUserId,
        AssignedMine = src.AssignedMine,
        UnassignedOnly = src.UnassignedOnly,
        SocialChannelId = src.SocialChannelId,
        Source = src.Source,
        CreatedFrom = src.CreatedFrom,
        CreatedTo = src.CreatedTo
    };
}
