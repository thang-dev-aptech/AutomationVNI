using Backend.Data;
using Backend.Modules.Crm.Customers;
using Backend.Modules.Notification;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Crm.Reminders;

public class CrmReminderService(
    AppDbContext db,
    IUserContext userContext,
    CrmCustomerService customers,
    NotificationService notifications)
{
    private static readonly TimeZoneInfo VietnamTz = ResolveVietnamTz();

    public async Task<CrmReminderResponse> CreateAsync(
        CreateCrmReminderRequest request, CancellationToken ct = default)
    {
        if (!await db.CrmCustomers.AnyAsync(x => x.Id == request.CrmCustomerId && !x.IsDeleted, ct))
            throw new KeyNotFoundException("Khách không tồn tại");
        var title = (request.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Tiêu đề nhắc việc bắt buộc");

        var entity = new CrmCustomerReminderModel
        {
            Id = Guid.NewGuid(),
            CrmCustomerId = request.CrmCustomerId,
            Title = title,
            DueAtUtc = DateTime.SpecifyKind(request.DueAtUtc, DateTimeKind.Utc),
            AssigneeUserId = request.AssigneeUserId ?? userContext.GetCurrentUserId(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userContext.GetCurrentUserName()
        };
        db.CrmCustomerReminders.Add(entity);
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(entity.CrmCustomerId, "CreateReminder", entity.Id.ToString(), ct);
        return await ToResponseAsync(entity, ct);
    }

    public async Task<CrmReminderResponse?> UpdateAsync(
        Guid id, UpdateCrmReminderRequest request, CancellationToken ct = default)
    {
        var entity = await db.CrmCustomerReminders.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (entity is null) return null;
        var title = (request.Title ?? "").Trim();
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Tiêu đề nhắc việc bắt buộc");

        entity.Title = title;
        entity.DueAtUtc = DateTime.SpecifyKind(request.DueAtUtc, DateTimeKind.Utc);
        entity.AssigneeUserId = request.AssigneeUserId;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = userContext.GetCurrentUserName();
        // Đổi hạn → cho phép thông báo lại nếu chưa hoàn thành
        if (!entity.IsCompleted)
            entity.NotifiedAtUtc = null;
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(entity.CrmCustomerId, "UpdateReminder", id.ToString(), ct);
        return await ToResponseAsync(entity, ct);
    }

    public async Task CompleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.CrmCustomerReminders.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
                     ?? throw new KeyNotFoundException("Nhắc việc không tồn tại");
        if (entity.IsCompleted) return;
        entity.IsCompleted = true;
        entity.CompletedAtUtc = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(entity.CrmCustomerId, "CompleteReminder", id.ToString(), ct);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.CrmCustomerReminders.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
                     ?? throw new KeyNotFoundException("Nhắc việc không tồn tại");
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        await customers.AddLogAsync(entity.CrmCustomerId, "DeleteReminder", id.ToString(), ct);
    }

    public async Task<CrmReminderBucketsResponse> ListBucketsAsync(
        Guid? assigneeUserId, CancellationToken ct = default)
    {
        var todayStartUtc = VietnamTodayStartUtc();
        var tomorrowStartUtc = todayStartUtc.AddDays(1);

        var query = db.CrmCustomerReminders.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.IsCompleted);
        if (assigneeUserId.HasValue)
            query = query.Where(x => x.AssigneeUserId == assigneeUserId.Value);

        var list = await query.OrderBy(x => x.DueAtUtc).ToListAsync(ct);
        var customerIds = list.Select(x => x.CrmCustomerId).Distinct().ToList();
        var names = await db.CrmCustomers.AsNoTracking()
            .Where(x => customerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var buckets = new CrmReminderBucketsResponse();
        foreach (var r in list)
        {
            var item = ToResponse(r, names.GetValueOrDefault(r.CrmCustomerId));
            if (r.DueAtUtc < todayStartUtc)
                buckets.Overdue.Add(item);
            else if (r.DueAtUtc < tomorrowStartUtc)
                buckets.Today.Add(item);
            else
                buckets.Upcoming.Add(item);
        }

        return buckets;
    }

    public async Task<IReadOnlyList<CrmReminderResponse>> ListForCustomerAsync(
        Guid customerId, CancellationToken ct = default)
    {
        if (!await db.CrmCustomers.AnyAsync(x => x.Id == customerId && !x.IsDeleted, ct))
            throw new KeyNotFoundException("Khách không tồn tại");

        var list = await db.CrmCustomerReminders.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CrmCustomerId == customerId)
            .OrderBy(x => x.IsCompleted)
            .ThenBy(x => x.DueAtUtc)
            .ToListAsync(ct);
        var name = await db.CrmCustomers.AsNoTracking()
            .Where(x => x.Id == customerId)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(ct);
        return list.Select(x => ToResponse(x, name)).ToList();
    }

    /// <summary>Worker: sinh thông báo cho nhắc đã tới hạn, đúng một lần.</summary>
    public async Task<int> NotifyDueAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var due = await db.CrmCustomerReminders
            .Where(x => !x.IsDeleted
                        && !x.IsCompleted
                        && x.NotifiedAtUtc == null
                        && x.DueAtUtc <= now)
            .ToListAsync(ct);

        var count = 0;
        foreach (var r in due)
        {
            var customerName = await db.CrmCustomers.AsNoTracking()
                .Where(x => x.Id == r.CrmCustomerId)
                .Select(x => x.DisplayName)
                .FirstOrDefaultAsync(ct) ?? "khách";

            await notifications.AddAsync(
                NotificationKind.CrmReminderDue,
                NotificationSource.System,
                "Hệ thống",
                $"Nhắc chăm sóc: {r.Title}",
                $"Khách {customerName} — đến hạn {r.DueAtUtc:u}",
                linkUrl: $"/customers/{r.CrmCustomerId}",
                refId: r.Id,
                ct);

            r.NotifiedAtUtc = now;
            r.UpdatedAt = now;
            count++;
        }

        if (count > 0)
            await db.SaveChangesAsync(ct);
        return count;
    }

    private async Task<CrmReminderResponse> ToResponseAsync(
        CrmCustomerReminderModel r, CancellationToken ct)
    {
        var name = await db.CrmCustomers.AsNoTracking()
            .Where(x => x.Id == r.CrmCustomerId)
            .Select(x => x.DisplayName)
            .FirstOrDefaultAsync(ct);
        return ToResponse(r, name);
    }

    private static CrmReminderResponse ToResponse(CrmCustomerReminderModel r, string? customerName) => new()
    {
        Id = r.Id,
        CrmCustomerId = r.CrmCustomerId,
        CustomerName = customerName,
        Title = r.Title,
        DueAtUtc = r.DueAtUtc,
        AssigneeUserId = r.AssigneeUserId,
        IsCompleted = r.IsCompleted,
        CompletedAtUtc = r.CompletedAtUtc,
        NotifiedAtUtc = r.NotifiedAtUtc,
        CreatedAt = r.CreatedAt
    };

    internal static DateTime VietnamTodayStartUtc()
    {
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, VietnamTz);
        var startLocal = localNow.Date;
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified), VietnamTz);
    }

    private static TimeZoneInfo ResolveVietnamTz()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone("VN", TimeSpan.FromHours(7), "VN", "VN");
    }
}
