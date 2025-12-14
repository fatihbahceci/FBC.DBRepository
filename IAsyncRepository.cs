using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace FBC.DBRepository;

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

    /// <summary>
    /// Asynchronously retrieves a paginated list of entities that match the specified criteria, with optional
    /// filtering, sorting, and related data inclusion.
    /// </summary>
    /// <remarks>If both pageNumber and itemsPerPage are set to 0, the method returns all matching entities
    /// without pagination. This method supports eager loading of related data and can be used for both tracked and
    /// untracked queries.</remarks>
    /// <param name="predicate">An expression to filter the entities to be included in the result. If null, all entities are considered.</param>
    /// <param name="orderBy">A function to order the resulting entities. If null, the default ordering is applied.</param>
    /// <param name="include">A function to specify related entities to include in the query results. Use to eagerly load navigation properties. If null, no related entities are included.</param>
    /// <param name="pageNumber">The zero-based index of the page to retrieve. Must be greater than or equal to 0.</param>
    /// <param name="itemsPerPage">The number of items to include in each page. Must be greater than or equal to 0. If 0, all items are returned after skipping to the specified page.</param>
    /// <param name="enableTracking">true to enable change tracking for the retrieved entities; otherwise, false. Disabling tracking can improve
    /// performance for read-only operations.</param>
    /// <param name="includeDeletedRecords">true to include entities marked as deleted in the results; otherwise, false.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a PaginateResponseModel<TEntity>
    /// with the paginated list of entities matching the specified criteria.</returns>
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
    Task<PaginateResponseModel<TEntity>> GetListAsync(
       IQueryable<TEntity> query,
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
