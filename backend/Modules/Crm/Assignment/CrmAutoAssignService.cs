using System.Text.Json;
using Backend.Data;
using Backend.Modules.PageMessage;
using Backend.Modules.Users;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Crm.Assignment;

public class CrmAutoAssignService(
    AppDbContext db,
    UsersService usersService,
    IUserContext userContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<CrmAutoAssignSettingsResponse> GetSettingsAsync(CancellationToken ct = default)
    {
        var settings = await EnsureSettingsAsync(ct);
        return ToResponse(settings);
    }

    public async Task<CrmAutoAssignSettingsResponse> UpdateSettingsAsync(
        UpdateCrmAutoAssignSettingsRequest request,
        CancellationToken ct = default)
    {
        var ids = (request.AssigneeUserIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        // Cho phép lưu cả người đang khoá trong danh sách — lúc gán sẽ bỏ qua.
        // Chỉ từ chối id không tồn tại.
        foreach (var id in ids)
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null)
                throw new InvalidOperationException($"Người dùng {id} không tồn tại");
        }

        var settings = await EnsureSettingsAsync(ct);
        settings.IsEnabled = request.IsEnabled;
        settings.AssigneeUserIdsJson = JsonSerializer.Serialize(ids, JsonOptions);
        settings.NextIndex = ids.Count == 0 ? 0 : settings.NextIndex % ids.Count;
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        return ToResponse(settings);
    }

    /// <summary>
    /// Gán người phụ trách cho hội thoại MỚI chưa có assignee khi tự chia đang bật.
    /// Compare-and-swap: chỉ cập nhật khi AssignedUserId còn null.
    /// </summary>
    /// <returns>true nếu gán thành công trong lượt này.</returns>
    public async Task<bool> TryAssignNewConversationAsync(
        Guid conversationId,
        CancellationToken ct = default)
    {
        var settings = await EnsureSettingsAsync(ct);
        if (!settings.IsEnabled) return false;

        var configuredIds = ParseAssigneeIds(settings.AssigneeUserIdsJson);
        if (configuredIds.Count == 0) return false;

        var activeAssignees = new List<(Guid Id, string DisplayName)>();
        foreach (var id in configuredIds)
        {
            var user = await usersService.FindActiveByIdAsync(id, ct);
            if (user is not null)
                activeAssignees.Add((user.Id, UsersService.ResolveDisplayName(user)));
        }

        if (activeAssignees.Count == 0) return false;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Đọc lại settings trong transaction để tăng NextIndex an toàn hơn khi song song.
        var liveSettings = await db.CrmAutoAssignSettings
            .SingleAsync(x => x.Id == CrmAutoAssignSettingsModel.SingletonId, ct);

        if (!liveSettings.IsEnabled)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        var start = liveSettings.NextIndex < 0
            ? 0
            : liveSettings.NextIndex % activeAssignees.Count;
        var pick = activeAssignees[start];

        var now = DateTime.UtcNow;
        var updated = await db.PageConversations
            .Where(x => x.Id == conversationId && !x.IsDeleted && x.AssignedUserId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.AssignedUserId, pick.Id)
                .SetProperty(x => x.AssignedTo, pick.DisplayName)
                .SetProperty(x => x.InboxStatus, MessageInboxStatus.InProgress)
                .SetProperty(x => x.UpdatedAt, now), ct);

        if (updated != 1)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        liveSettings.NextIndex = (start + 1) % activeAssignees.Count;
        liveSettings.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        db.MessageActionLogs.Add(new MessageActionLogModel
        {
            Id = Guid.NewGuid(),
            PageConversationId = conversationId,
            ActionType = MessageActionType.Assign,
            ActorUserId = null,
            ActorUserName = "system:auto-assign",
            PayloadJson = JsonSerializer.Serialize(new
            {
                assignedUserId = pick.Id,
                assignedTo = pick.DisplayName,
                source = "auto-assign"
            }, JsonOptions),
            Success = true,
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return true;
    }

    private async Task<CrmAutoAssignSettingsModel> EnsureSettingsAsync(CancellationToken ct)
    {
        var settings = await db.CrmAutoAssignSettings
            .FirstOrDefaultAsync(x => x.Id == CrmAutoAssignSettingsModel.SingletonId, ct);
        if (settings is not null) return settings;

        settings = new CrmAutoAssignSettingsModel
        {
            Id = CrmAutoAssignSettingsModel.SingletonId,
            IsEnabled = false,
            AssigneeUserIdsJson = "[]",
            NextIndex = 0,
            CreatedAt = DateTime.UtcNow
        };
        db.CrmAutoAssignSettings.Add(settings);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(settings).State = EntityState.Detached;
            settings = await db.CrmAutoAssignSettings
                .SingleAsync(x => x.Id == CrmAutoAssignSettingsModel.SingletonId, ct);
        }

        return settings;
    }

    private static List<Guid> ParseAssigneeIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static CrmAutoAssignSettingsResponse ToResponse(CrmAutoAssignSettingsModel settings)
        => new()
        {
            IsEnabled = settings.IsEnabled,
            AssigneeUserIds = ParseAssigneeIds(settings.AssigneeUserIdsJson),
            NextIndex = settings.NextIndex
        };
}
