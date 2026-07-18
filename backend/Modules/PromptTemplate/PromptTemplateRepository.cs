using Backend.Data;
using Backend.Modules.PromptTemplate.Enums;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.PromptTemplate;

public class PromptTemplateRepository : GenericRepository<PromptTemplateModel>
{
    public PromptTemplateRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<PromptTemplateResponse>> FilterAsync(
        PromptTemplateFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(x => x.Name.Contains(kw) || (x.Description != null && x.Description.Contains(kw)));
        }

        if (request.TemplateType.HasValue)
            query = query.Where(x => x.TemplateType == request.TemplateType.Value);

        if (request.IsActive.HasValue)
            query = query.Where(x => x.IsActive == request.IsActive.Value);

        if (request.IsDefault.HasValue)
            query = query.Where(x => x.IsDefault == request.IsDefault.Value);

        query = query.OrderByDescending(x => x.IsDefault).ThenByDescending(x => x.CreatedAt);

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        return new PagedResult<PromptTemplateResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<PromptTemplateModel> CreateAsync(
        CreatePromptTemplateRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Tên template không được để trống");
        if (string.IsNullOrWhiteSpace(request.Body))
            throw new ArgumentException("Nội dung prompt không được để trống");

        var entity = new PromptTemplateModel
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            TemplateType = request.TemplateType,
            Body = request.Body.Trim(),
            IsDefault = request.IsDefault,
            IsActive = request.IsActive
        };

        var created = await base.CreateAsync(entity, ct);

        // Chỉ một default mỗi loại — nếu template mới là default, gỡ default ở các template khác cùng loại.
        if (created.IsDefault)
            await UnsetOtherDefaultsAsync(created.TemplateType, created.Id, ct);

        return created;
    }

    public async Task<PromptTemplateModel?> UpdateAsync(
        Guid id, UpdatePromptTemplateRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.Name is not null) entity.Name = request.Name.Trim();
        if (request.Description is not null) entity.Description = request.Description.Trim();
        if (request.Body is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Body))
                throw new ArgumentException("Nội dung prompt không được để trống");
            entity.Body = request.Body.Trim();
        }
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;
        if (request.IsDefault.HasValue) entity.IsDefault = request.IsDefault.Value;

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);

        if (entity.IsDefault)
            await UnsetOtherDefaultsAsync(entity.TemplateType, entity.Id, ct);

        return entity;
    }

    /// <summary>Template mặc định đang hoạt động của một loại, hoặc null.</summary>
    public async Task<PromptTemplateModel?> GetDefaultAsync(
        PromptTemplateType type, CancellationToken ct = default)
        => await QueryActive()
            .Where(x => x.TemplateType == type && x.IsDefault && x.IsActive)
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>Lấy template theo Id nếu còn hoạt động (dùng khi resolve từ Post/PageContext).</summary>
    public async Task<PromptTemplateModel?> GetActiveByIdAsync(Guid id, CancellationToken ct = default)
        => await QueryActive().FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);

    private async Task UnsetOtherDefaultsAsync(PromptTemplateType type, Guid keepId, CancellationToken ct)
    {
        var others = await QueryActive()
            .Where(x => x.TemplateType == type && x.IsDefault && x.Id != keepId)
            .ToListAsync(ct);
        if (others.Count == 0) return;

        foreach (var o in others)
        {
            o.IsDefault = false;
            ApplyUpdateAudit(o);
        }
        await Context.SaveChangesAsync(ct);
    }

    public static PromptTemplateResponse ToResponse(PromptTemplateModel e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Description = e.Description,
        TemplateType = e.TemplateType,
        Body = e.Body,
        IsDefault = e.IsDefault,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt
    };
}
