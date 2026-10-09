using Backend.Data;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Crm.Opportunities;

public class CrmOpportunityStageService(AppDbContext db, IUserContext userContext)
{
    public async Task<IReadOnlyList<CrmOpportunityStageResponse>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.CrmOpportunityStages.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);
        return rows.Select(ToResponse).ToList();
    }

    public async Task<CrmOpportunityStageResponse> CreateAsync(
        CreateCrmOpportunityStageRequest request, CancellationToken ct = default)
    {
        var name = (request.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Tên giai đoạn bắt buộc");

        var maxOrder = await db.CrmOpportunityStages
            .Where(x => !x.IsDeleted)
            .Select(x => (int?)x.SortOrder)
            .MaxAsync(ct) ?? 0;

        var entity = new CrmOpportunityStageModel
        {
            Id = Guid.NewGuid(),
            Name = name,
            Color = string.IsNullOrWhiteSpace(request.Color) ? "#6B7280" : request.Color.Trim(),
            Kind = request.Kind,
            SortOrder = request.SortOrder ?? (maxOrder + 1),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = userContext.GetCurrentUserName()
        };
        db.CrmOpportunityStages.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task<CrmOpportunityStageResponse?> UpdateAsync(
        Guid id, UpdateCrmOpportunityStageRequest request, CancellationToken ct = default)
    {
        var entity = await db.CrmOpportunityStages
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (entity is null) return null;

        var name = (request.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Tên giai đoạn bắt buộc");

        // Không cho đổi Kind nếu đó là giai đoạn Kind cuối cùng.
        if (entity.Kind != request.Kind)
            await EnsureKindRemainsAsync(entity.Id, entity.Kind, ct);

        entity.Name = name;
        entity.Color = string.IsNullOrWhiteSpace(request.Color) ? entity.Color : request.Color.Trim();
        entity.Kind = request.Kind;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
        return ToResponse(entity);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.CrmOpportunityStages
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Giai đoạn không tồn tại");

        var hasOpps = await db.CrmOpportunities.AnyAsync(
            x => !x.IsDeleted && x.StageId == id, ct);
        if (hasOpps)
            throw new InvalidOperationException("Không xoá được giai đoạn còn cơ hội — hãy chuyển cơ hội đi trước");

        await EnsureKindRemainsAsync(id, entity.Kind, ct);

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userContext.GetCurrentUserName();
        await db.SaveChangesAsync(ct);
    }

    public async Task ReorderAsync(ReorderCrmOpportunityStagesRequest request, CancellationToken ct = default)
    {
        var ids = request.StageIds?.Where(x => x != Guid.Empty).Distinct().ToList() ?? [];
        if (ids.Count == 0)
            throw new InvalidOperationException("Danh sách giai đoạn trống");

        var stages = await db.CrmOpportunityStages
            .Where(x => !x.IsDeleted && ids.Contains(x.Id))
            .ToListAsync(ct);
        if (stages.Count != ids.Count)
            throw new InvalidOperationException("Một hoặc nhiều giai đoạn không tồn tại");

        for (var i = 0; i < ids.Count; i++)
        {
            var stage = stages.First(x => x.Id == ids[i]);
            stage.SortOrder = i + 1;
            stage.UpdatedAt = DateTime.UtcNow;
            stage.UpdatedBy = userContext.GetCurrentUserName();
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureKindRemainsAsync(
        Guid excludingId, CrmOpportunityStageKind kind, CancellationToken ct)
    {
        var others = await db.CrmOpportunityStages.CountAsync(
            x => !x.IsDeleted && x.Id != excludingId && x.Kind == kind, ct);
        if (others == 0)
            throw new InvalidOperationException(
                $"Phải còn ít nhất một giai đoạn {kind}");
    }

    private static CrmOpportunityStageResponse ToResponse(CrmOpportunityStageModel e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Color = e.Color,
        SortOrder = e.SortOrder,
        Kind = e.Kind
    };
}
