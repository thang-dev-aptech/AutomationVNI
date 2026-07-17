using Backend.Data;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Category;

public class CategoryRepository : GenericRepository<CategoryModel>
{
    public CategoryRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<CategoryResponse>> FilterAsync(
        CategoryFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim();
            query = query.Where(x => x.Name.Contains(kw) || x.Slug.Contains(kw));
        }

        if (request.ParentCategoryId.HasValue)
            query = query.Where(x => x.ParentCategoryId == request.ParentCategoryId.Value);

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        return new PagedResult<CategoryResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<CategoryModel> CreateAsync(
        CreateCategoryRequest request, CancellationToken ct = default)
    {
        var slugExists = await QueryActive()
            .AnyAsync(x => x.Slug == request.Slug.Trim(), ct);
        if (slugExists)
            throw new InvalidOperationException($"Slug '{request.Slug}' đã tồn tại.");

        var entity = new CategoryModel
        {
            Name = request.Name.Trim(),
            Slug = request.Slug.Trim().ToLowerInvariant(),
            Description = request.Description?.Trim(),
            ParentCategoryId = request.ParentCategoryId
        };

        return await base.CreateAsync(entity, ct);
    }

    public async Task<CategoryModel?> UpdateAsync(
        Guid id, UpdateCategoryRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.Name is not null) entity.Name = request.Name.Trim();
        if (request.Description is not null) entity.Description = request.Description.Trim();
        if (request.ParentCategoryId.HasValue) entity.ParentCategoryId = request.ParentCategoryId;

        if (request.Slug is not null)
        {
            var slug = request.Slug.Trim().ToLowerInvariant();
            var slugTaken = await QueryActive()
                .AnyAsync(x => x.Slug == slug && x.Id != id, ct);
            if (slugTaken)
                throw new InvalidOperationException($"Slug '{slug}' đã tồn tại.");
            entity.Slug = slug;
        }

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public static CategoryResponse ToResponse(CategoryModel e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Slug = e.Slug,
        Description = e.Description,
        ParentCategoryId = e.ParentCategoryId,
        CreatedAt = e.CreatedAt
    };
}
