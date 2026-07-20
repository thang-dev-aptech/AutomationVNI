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

        // Default list = category packs only (text + image in one row).
        if (request.TemplateType.HasValue)
            query = query.Where(x => x.TemplateType == request.TemplateType.Value);
        else
            query = query.Where(x => x.TemplateType == PromptTemplateType.Category);

        if (request.IsActive.HasValue)
            query = query.Where(x => x.IsActive == request.IsActive.Value);

        if (request.IsDefault.HasValue)
            query = query.Where(x => x.IsDefault == request.IsDefault.Value);

        query = query.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name);

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
            throw new ArgumentException("Tên danh mục không được để trống");
        if (string.IsNullOrWhiteSpace(request.TextBody))
            throw new ArgumentException("Prompt text không được để trống");
        if (string.IsNullOrWhiteSpace(request.ImageBody))
            throw new ArgumentException("Prompt ảnh không được để trống");

        var name = request.Name.Trim();
        var dup = await QueryActive()
            .AnyAsync(x => x.TemplateType == PromptTemplateType.Category
                           && x.Name.ToLower() == name.ToLower(), ct);
        if (dup)
            throw new ArgumentException($"Đã có template cho danh mục \"{name}\"");

        var entity = new PromptTemplateModel
        {
            Name = name,
            Description = request.Description?.Trim(),
            TemplateType = PromptTemplateType.Category,
            TextBody = request.TextBody.Trim(),
            ImageBody = request.ImageBody.Trim(),
            Body = request.TextBody.Trim(), // legacy mirror
            IsDefault = request.IsDefault,
            IsActive = request.IsActive
        };

        var created = await base.CreateAsync(entity, ct);

        if (created.IsDefault)
            await UnsetOtherCategoryDefaultsAsync(created.Id, ct);

        return created;
    }

    public async Task<PromptTemplateModel?> UpdateAsync(
        Guid id, UpdatePromptTemplateRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Tên danh mục không được để trống");

            var dup = await QueryActive()
                .AnyAsync(x => x.Id != id
                               && x.TemplateType == PromptTemplateType.Category
                               && x.Name.ToLower() == name.ToLower(), ct);
            if (dup)
                throw new ArgumentException($"Đã có template cho danh mục \"{name}\"");

            entity.Name = name;
        }

        if (request.Description is not null) entity.Description = request.Description.Trim();
        if (request.TextBody is not null)
        {
            if (string.IsNullOrWhiteSpace(request.TextBody))
                throw new ArgumentException("Prompt text không được để trống");
            entity.TextBody = request.TextBody.Trim();
            entity.Body = entity.TextBody;
        }
        if (request.ImageBody is not null)
        {
            if (string.IsNullOrWhiteSpace(request.ImageBody))
                throw new ArgumentException("Prompt ảnh không được để trống");
            entity.ImageBody = request.ImageBody.Trim();
        }
        if (request.IsActive.HasValue) entity.IsActive = request.IsActive.Value;
        if (request.IsDefault.HasValue) entity.IsDefault = request.IsDefault.Value;

        // Promote legacy typed row to Category when both bodies present.
        if (!string.IsNullOrWhiteSpace(entity.TextBody) && !string.IsNullOrWhiteSpace(entity.ImageBody))
            entity.TemplateType = PromptTemplateType.Category;

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);

        if (entity.IsDefault && entity.TemplateType == PromptTemplateType.Category)
            await UnsetOtherCategoryDefaultsAsync(entity.Id, ct);

        return entity;
    }

    /// <summary>Category pack mặc định đang hoạt động, hoặc null.</summary>
    public async Task<PromptTemplateModel?> GetDefaultCategoryAsync(CancellationToken ct = default)
        => await QueryActive()
            .Where(x => x.TemplateType == PromptTemplateType.Category && x.IsDefault && x.IsActive)
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    /// <summary>Legacy default theo loại Text/Image.</summary>
    public async Task<PromptTemplateModel?> GetDefaultAsync(
        PromptTemplateType type, CancellationToken ct = default)
        => await QueryActive()
            .Where(x => x.TemplateType == type && x.IsDefault && x.IsActive)
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<PromptTemplateModel?> GetActiveByIdAsync(Guid id, CancellationToken ct = default)
        => await QueryActive().FirstOrDefaultAsync(x => x.Id == id && x.IsActive, ct);

    public async Task<BulkImportPromptTemplatesResult> BulkImportAsync(
        BulkImportPromptTemplatesRequest request, CancellationToken ct = default)
    {
        var result = new BulkImportPromptTemplatesResult();
        var items = (request.Items ?? [])
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .ToList();

        if (items.Count == 0)
            throw new ArgumentException("Danh sách template trống");

        foreach (var item in items)
        {
            var name = item.Name.Trim();
            try
            {
                if (string.IsNullOrWhiteSpace(item.TextBody) || string.IsNullOrWhiteSpace(item.ImageBody))
                {
                    result.Skipped++;
                    result.Errors.Add($"\"{name}\": thiếu textBody hoặc imageBody");
                    continue;
                }

                var existing = await QueryActive()
                    .FirstOrDefaultAsync(
                        x => x.TemplateType == PromptTemplateType.Category
                             && x.Name.ToLower() == name.ToLower(),
                        ct);

                if (existing is not null)
                {
                    if (!request.UpdateExisting)
                    {
                        result.Skipped++;
                        continue;
                    }

                    await UpdateAsync(existing.Id, new UpdatePromptTemplateRequest
                    {
                        Name = name,
                        Description = item.Description,
                        TextBody = item.TextBody,
                        ImageBody = item.ImageBody,
                        IsDefault = item.IsDefault,
                        IsActive = item.IsActive
                    }, ct);
                    result.Updated++;
                    continue;
                }

                await CreateAsync(new CreatePromptTemplateRequest
                {
                    Name = name,
                    Description = item.Description,
                    TextBody = item.TextBody,
                    ImageBody = item.ImageBody,
                    IsDefault = item.IsDefault,
                    IsActive = item.IsActive
                }, ct);
                result.Created++;
            }
            catch (Exception ex)
            {
                result.Skipped++;
                result.Errors.Add($"\"{name}\": {ex.Message}");
            }
        }

        result.Message =
            $"Import xong: tạo {result.Created}, cập nhật {result.Updated}, bỏ qua {result.Skipped}";
        return result;
    }

    private async Task UnsetOtherCategoryDefaultsAsync(Guid keepId, CancellationToken ct)
    {
        var others = await QueryActive()
            .Where(x => x.TemplateType == PromptTemplateType.Category && x.IsDefault && x.Id != keepId)
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
        TextBody = e.TextBody,
        ImageBody = e.ImageBody,
        Body = e.Body,
        IsDefault = e.IsDefault,
        IsActive = e.IsActive,
        CreatedAt = e.CreatedAt
    };
}
