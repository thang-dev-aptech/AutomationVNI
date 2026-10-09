using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Crm.Audit;

public class CrmAuditService(AppDbContext db)
{
    public async Task<IReadOnlyList<CrmAuditLogResponse>> ListAsync(
        Guid? customerId, int take = 100, CancellationToken ct = default)
    {
        var query = db.CrmCustomerActionLogs.AsNoTracking().Where(x => !x.IsDeleted);
        if (customerId.HasValue)
            query = query.Where(x => x.CrmCustomerId == customerId.Value);

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(x => new CrmAuditLogResponse
            {
                Id = x.Id,
                CrmCustomerId = x.CrmCustomerId,
                ActionType = x.ActionType,
                ActorUserId = x.ActorUserId,
                ActorUserName = x.ActorUserName,
                PayloadJson = x.PayloadJson,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(ct);
    }
}
