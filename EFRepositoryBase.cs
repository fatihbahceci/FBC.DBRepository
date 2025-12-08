using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace FBC.Repository;

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
    private IQueryable<TEntity> prepareQuery(
                                         Expression<Func<TEntity, bool>> predicate,
                                         Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
                                         bool enableTracking = true,
                                         bool includeDeletedRecords = false)
    {
        IQueryable<TEntity> queryable = GetQueryable();
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
        queryable = queryable.Where(predicate);
        return queryable;
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

    public async Task<PaginateResponseModel<TEntity>> GetListAsync(Expression<Func<TEntity, bool>>? predicate = null,
                                                                   Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
                                                                   Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
                                                                   int index = 0,
                                                                   int size = 0,
                                                                   bool enableTracking = true,
                                                                   bool includeDeletedRecords = false,
                                                                   CancellationToken cancellationToken = default)
    {
        var q = prepareQuery(predicate, include, enableTracking, includeDeletedRecords);

        if (orderBy != null)
            return await orderBy(q).ToPaginateAsync(index, size, cancellationToken);
        else
            return await q.ToPaginateAsync(index, size, cancellationToken);
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
    #endregion
}
