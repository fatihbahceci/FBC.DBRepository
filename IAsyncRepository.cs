using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace FBC.Repository;

public interface IAsyncRepository<TEntity, TEntityId> : IQuery<TEntity>
    where TEntity : Entity<TEntityId, TEntity>
    where TEntityId : IEquatable<TEntityId>

{
    Task<TEntity?> GetAsync(
        Expression<Func<TEntity, bool>> predicate,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        bool enableTracking = true,
        bool includeDeletedRecords = false,
        CancellationToken cancellationToken = default);

    Task<PaginateResponseModel<TEntity>> GetListAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        int start = 0,
        int offsett = 0,
        bool enableTracking = true,
        bool includeDeletedRecords = false,
        CancellationToken cancellationToken = default
    );


    Task<bool> AnyAsync(
      Expression<Func<TEntity, bool>>? predicate = null,
      bool enableTracking = true,
      bool includeDeletedRecords = false,
      CancellationToken cancellationToken = default
    );

    Task<TEntity> ApplyOperation(EntityOperation operationType, TEntity entity, bool alsoValidate, bool deletePermanent = false);
    Task<ICollection<TEntity>> ApplyOperationRange(EntityOperation operationType, ICollection<TEntity> entities, bool alsoValidate, bool deletePermanent = false);

}
