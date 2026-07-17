using Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend.Shared.Repositories;

public class GenericRepository<TEntity> : IGenericRepository<TEntity>
    where TEntity : BaseEntity
{
    protected readonly AppDbContext Context;
    protected readonly DbSet<TEntity> DbSet;
    protected readonly IUserContext UserContext;

    public GenericRepository(AppDbContext context, IUserContext userContext)
    {
        Context = context;
        DbSet = context.Set<TEntity>();
        UserContext = userContext;
    }

    public virtual IQueryable<TEntity> QueryActive() =>
        DbSet.Where(x => !x.IsDeleted);

    public virtual async Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default) =>
        await QueryActive()
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public virtual async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await QueryActive()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public virtual async Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        if (entity.Id == Guid.Empty)
            entity.Id = Guid.NewGuid();

        ApplyCreateAudit(entity);
        DbSet.Add(entity);
        await Context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public virtual async Task<List<TEntity>> MultiCreateAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        var list = entities.ToList();
        foreach (var entity in list)
        {
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();
            ApplyCreateAudit(entity);
        }

        DbSet.AddRange(list);
        await Context.SaveChangesAsync(cancellationToken);
        return list;
    }

    public virtual async Task<TEntity?> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        var existing = await GetByIdAsync(entity.Id, cancellationToken);
        if (existing is null)
            return null;

        var createdAt = existing.CreatedAt;
        var createdBy = existing.CreatedBy;

        Context.Entry(existing).CurrentValues.SetValues(entity);
        existing.CreatedAt = createdAt;
        existing.CreatedBy = createdBy;
        ApplyUpdateAudit(existing);
        await Context.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public virtual async Task<List<TEntity>> MultiUpdateAsync(
        IEnumerable<TEntity> entities,
        CancellationToken cancellationToken = default)
    {
        var updated = new List<TEntity>();
        foreach (var entity in entities)
        {
            var result = await UpdateAsync(entity, cancellationToken);
            if (result is not null)
                updated.Add(result);
        }

        return updated;
    }

    public virtual async Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, cancellationToken);
        if (entity is null)
            return false;

        ApplySoftDeleteAudit(entity);
        await Context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public virtual async Task<int> MultiSoftDeleteAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var id in ids)
        {
            if (await SoftDeleteAsync(id, cancellationToken))
                count++;
        }

        return count;
    }

    protected virtual void ApplyCreateAudit(TEntity entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.CreatedBy = GetCurrentUserName();
        entity.IsDeleted = false;
    }

    protected virtual void ApplyUpdateAudit(TEntity entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = GetCurrentUserName();
    }

    protected virtual void ApplySoftDeleteAudit(TEntity entity)
    {
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = GetCurrentUserName();
    }

    protected Guid GetCurrentUserId() =>
        UserContext.GetCurrentUserId() ?? Guid.Empty;

    protected string? GetCurrentUserName() =>
        UserContext.GetCurrentUserName();

    // Public helper cho BaseController — filter keyword cơ bản không cần override
    public async Task<PagedResult<TEntity>> PaginatePublicAsync(
        string? keyword,
        int index,
        int size,
        CancellationToken cancellationToken = default)
    {
        return await PaginateAsync(QueryActive(), index, size, cancellationToken);
    }

    protected async Task<PagedResult<TEntity>> PaginateAsync(
        IQueryable<TEntity> query,
        int index,
        int size,
        CancellationToken cancellationToken = default)
    {
        var safeIndex = index < 1 ? 1 : index;
        var safeSize = size < 1 ? 20 : size;

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((safeIndex - 1) * safeSize)
            .Take(safeSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TEntity>
        {
            Items = items,
            Total = total,
            Index = safeIndex,
            Size = safeSize
        };
    }
}
