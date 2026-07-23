using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Backend.Data;
using Backend.Shared;
using Backend.Shared.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Category;

public class CategoryRepository : GenericRepository<CategoryModel>
{
    public CategoryRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    /// <summary>Sinh slug từ tên tiếng Việt: bỏ dấu, đ→d, còn chữ-số nối bằng "-".</summary>
    public static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var s = name.Trim().ToLowerInvariant().Replace('đ', 'd');
        s = s.Normalize(NormalizationForm.FormD);
        s = string.Concat(s.Where(c =>
            CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));
        s = Regex.Replace(s, @"[^a-z0-9]+", "-").Trim('-');
        return s;
    }

    /// <summary>Import nhanh: mỗi tên → 1 loại bài; slug trùng (hiện có hoặc trong batch) thì bỏ qua.</summary>
    public async Task<CategoryImportResult> ImportAsync(
        List<string> names, Guid? parentCategoryId, CancellationToken ct = default)
    {
        var result = new CategoryImportResult();
        var existingSlugs = (await QueryActive().Select(x => x.Slug).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in names)
        {
            var name = raw?.Trim();
            if (string.IsNullOrWhiteSpace(name)) { result.Skipped++; continue; }

            var slug = Slugify(name);
            if (string.IsNullOrWhiteSpace(slug) || existingSlugs.Contains(slug))
            {
                result.Skipped++;
                continue;
            }

            try
            {
                var entity = await base.CreateAsync(new CategoryModel
                {
                    Name = name,
                    Slug = slug,
                    ParentCategoryId = parentCategoryId
                }, ct);
                existingSlugs.Add(slug);
                result.Created++;
                result.Items.Add(ToResponse(entity));
            }
            catch (Exception ex)
            {
                if (result.Errors.Count < 20) result.Errors.Add($"{name}: {ex.Message}");
            }
        }

        return result;
    }

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
