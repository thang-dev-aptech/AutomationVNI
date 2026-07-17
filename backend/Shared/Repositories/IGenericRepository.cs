namespace Backend.Shared.Repositories;

public interface IGenericRepository<TEntity> where TEntity : BaseEntity
{
    Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TEntity> CreateAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task<List<TEntity>> MultiCreateAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<TEntity?> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task<List<TEntity>> MultiUpdateAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> MultiSoftDeleteAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    IQueryable<TEntity> QueryActive();
}
