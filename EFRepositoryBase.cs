using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace FBC.DBRepository;

public abstract class EFRepositoryBase<TEntity, TEntityId, TContext>
    : IAsyncRepository<TEntity, TEntityId>/* ,IRepository<TEntity, TEntityId>*/
    where TEntity : Entity<TEntityId, TEntity>
    where TEntityId : IEquatable<TEntityId>
    where TContext : DbContext
{
    protected readonly TContext _context;
    public EFRepositoryBase(TContext context)
    {
        _context = context;
    }

    public IQueryable<TEntity> GetQueryable() => _context.Set<TEntity>();


    #region IAsyncRepository Implementation
    private IQueryable<TEntity> updateQuery(
        IQueryable<TEntity> queryable,
        Expression<Func<TEntity, bool>>? predicate,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        bool enableTracking = true,
        bool includeDeletedRecords = false)
    {
        if (!enableTracking)
            queryable = queryable.AsNoTracking();
        if (include != null)
            queryable = include(queryable);
        //if (withDeleted)
        //    queryable = queryable.IgnoreQueryFilters();
        if (!includeDeletedRecords && typeof(IEntityHasSoftDeleteFeature).IsAssignableFrom(typeof(TEntity)))
        {
            //queryable = queryable.Where(x => x.IsDeleted == false);
            queryable = queryable.Where(x => !((IEntityHasSoftDeleteFeature)x).IsDeleted);

        }
        if (predicate != null)
            queryable = queryable.Where(predicate);
        return queryable;
    }
    private IQueryable<TEntity> prepareQuery(
        Expression<Func<TEntity, bool>>? predicate,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        bool enableTracking = true,
        bool includeDeletedRecords = false)
    {
        IQueryable<TEntity> queryable = GetQueryable();
        return updateQuery(queryable, predicate, include, enableTracking, includeDeletedRecords);
    }
    public async Task<TEntity?> GetAsync(Expression<Func<TEntity, bool>> predicate,
                                         Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
                                         bool enableTracking = true,
                                         bool includeDeletedRecords = false,
                                         CancellationToken cancellationToken = default)
    {
        var q = prepareQuery(predicate, include, enableTracking, includeDeletedRecords);
        return await q.FirstOrDefaultAsync(cancellationToken);
    }
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
    public async Task<PaginateResponseModel<TEntity>>
        GetListAsync(Expression<Func<TEntity, bool>>? predicate = null,
            Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
            Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
            int pageNumber = 0,
            int itemsPerPage = 0,
            bool enableTracking = true,
            bool includeDeletedRecords = false,
            CancellationToken cancellationToken = default)
    {
        var q = prepareQuery(predicate, include, enableTracking, includeDeletedRecords);

        if (orderBy != null)
            return await orderBy(q).ToPaginateAsync(pageNumber, itemsPerPage, cancellationToken);
        else
            return await q.ToPaginateAsync(pageNumber, itemsPerPage, cancellationToken);
    }

    public async Task<bool> AnyAsync(
      Expression<Func<TEntity, bool>>? predicate = null,
      bool enableTracking = true,
      bool includeDeletedRecords = false,
      CancellationToken cancellationToken = default
    )
    {
        var q = prepareQuery(predicate, null, enableTracking, includeDeletedRecords);
        return await q.AnyAsync(cancellationToken);
    }

    public async Task<ICollection<TEntity>> ApplyOperationRange(EntityOperation entityOperation, ICollection<TEntity> entities, bool alsoValidate, bool deletePermanent = false)
    {
        foreach (var entity in entities)
        {
            await entity.CheckEntityDataForAsync(entityOperation, alsoValidate, deletePermanent, GetQueryable);
        }
        switch (entityOperation)
        {
            case EntityOperation.Create:
                await _context.AddRangeAsync(entities);
                break;
            case EntityOperation.Update:
                _context.UpdateRange(entities);
                break;
            case EntityOperation.Delete:
                if (!deletePermanent)
                {
                    _context.UpdateRange(entities);
                }
                else
                {
                    _context.RemoveRange(entities);
                }
                break;
        }
        await _context.SaveChangesAsync();
        return entities;
    }
    public async Task<TEntity> ApplyOperation(EntityOperation entityOperation, TEntity entity, bool alsoValidate, bool deletePermanent = false)
    {
        await entity.CheckEntityDataForAsync(entityOperation, alsoValidate, deletePermanent, GetQueryable);
        switch (entityOperation)
        {
            case EntityOperation.Create:
                await _context.AddAsync(entity);
                break;
            case EntityOperation.Update:
                _context.Update(entity);
                break;
            case EntityOperation.Delete:
                if (!deletePermanent)
                {
                    _context.Update(entity);
                }
                else
                {
                    _context.Remove(entity);
                }
                break;
        }
        await _context.SaveChangesAsync();
        return entity;
    }

    public Task<PaginateResponseModel<TEntity>> GetListAsync(IQueryable<TEntity> query, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null, int start = 0, int offsett = 0, bool enableTracking = true, bool includeDeletedRecords = false, CancellationToken cancellationToken = default)
    {
        var q = updateQuery(query, null, include, enableTracking, includeDeletedRecords);
        if (orderBy != null)
            return orderBy(q).ToPaginateAsync(start, offsett, cancellationToken);
        else
            return q.ToPaginateAsync(start, offsett, cancellationToken);

    }
    #endregion
}
