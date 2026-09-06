using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;

namespace FBC.DBRepository;

public abstract class EFRepositoryBase<TEntity, TEntityId, TContext>
    : IAsyncRepository<TEntity, TEntityId>
    where TEntity : Entity<TEntityId, TEntity>
    where TEntityId : IEquatable<TEntityId>
    where TContext : DbContext
{
    protected readonly TContext _context;
    protected readonly ICurrentUserProvider? _currentUserProvider;
    private IDbContextTransaction? _transaction;

    public EFRepositoryBase(TContext context) : this(context, null) { }

    public EFRepositoryBase(TContext context, ICurrentUserProvider? currentUserProvider)
    {
        _context = context;
        _currentUserProvider = currentUserProvider;
    }

    /// <summary>
    /// Override this to provide the current user identifier for audit fields (CreatedBy, UpdatedBy, DeletedBy).
    /// If an ICurrentUserProvider is injected, this returns the provider's UserId by default.
    /// </summary>
    protected virtual string? GetCurrentUser() => _currentUserProvider?.GetUserId();

    public IQueryable<TEntity> GetQueryable() => _context.Set<TEntity>();

    /// <summary>
    /// <see cref="GetQueryable"/> with the soft-delete filter already applied.
    /// </summary>
    /// <remarks>
    /// Declared here as well as on the interface: a default interface member is only reachable through
    /// the interface, and repositories are just as often held by their concrete type.
    /// </remarks>
    public IQueryable<TEntity> GetActiveQueryable() => prepareQuery(predicate: null);


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
        if (!includeDeletedRecords && typeof(IEntityHasSoftDeleteFeature).IsAssignableFrom(typeof(TEntity)))
        {
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

    public async Task<TEntity?> GetByIdAsync(TEntityId id,
                                              Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
                                              bool enableTracking = true,
                                              bool includeDeletedRecords = false,
                                              CancellationToken cancellationToken = default)
    {
        return await GetAsync(e => e.Id.Equals(id), include, enableTracking, includeDeletedRecords, cancellationToken);
    }

    public async Task<IList<TEntity>> GetByIdsAsync(
        IEnumerable<TEntityId> ids,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        bool enableTracking = true,
        bool includeDeletedRecords = false,
        CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        var q = prepareQuery(e => idList.Contains(e.Id), include, enableTracking, includeDeletedRecords);
        return await q.ToListAsync(cancellationToken);
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

    public async Task<int> CountAsync(
      Expression<Func<TEntity, bool>>? predicate = null,
      bool includeDeletedRecords = false,
      CancellationToken cancellationToken = default
    )
    {
        var q = prepareQuery(predicate, null, false, includeDeletedRecords);
        return await q.CountAsync(cancellationToken);
    }

    /// <summary>
    /// Checks that the current user holds one of the roles the entity requires for this operation.
    /// </summary>
    /// <remarks>
    /// <para><b>This check is skipped entirely when no <see cref="ICurrentUserProvider"/> was supplied.</b>
    /// A repository constructed as <c>new XRepository(context)</c> — from a seeder, a migration, a
    /// background service or a test — enforces nothing, and says nothing about it. That default is
    /// deliberate: those paths have no user to check against, and failing them closed would stop
    /// applications from starting.</para>
    /// <para>It is also a fail-open default, which is the wrong way round for anything that reaches
    /// a request. That is why this method is <c>virtual</c>: an application whose repositories always
    /// run with a user can override it and refuse instead.</para>
    /// <code>
    /// protected override void CheckRoleRequirement(EntityOperation operation, TEntity entity)
    /// {
    ///     if (entity is IEntityRequiresRole &amp;&amp; _currentUserProvider is null)
    ///         throw new InvalidOperationException(
    ///             $"{typeof(TEntity).Name} declares required roles but this repository has no user provider.");
    ///
    ///     base.CheckRoleRequirement(operation, entity);
    /// }
    /// </code>
    /// </remarks>
    protected virtual void CheckRoleRequirement(EntityOperation operationType, TEntity entity)
    {
        if (entity is IEntityRequiresRole roleEntity && _currentUserProvider != null)
        {
            var requiredRoles = roleEntity.GetRequiredRolesFor(operationType);
            if (requiredRoles.Length > 0 && !requiredRoles.Any(r => _currentUserProvider.IsInRole(r)))
            {
                throw new UnauthorizedAccessException(
                    $"User does not have the required role to perform '{operationType}' on '{typeof(TEntity).Name}'.");
            }
        }
    }

    public Task<ICollection<TEntity>> ApplyOperationRange(EntityOperation entityOperation, ICollection<TEntity> entities, bool alsoValidate, bool deletePermanent = false)
        => ApplyOperationRange(entityOperation, entities, alsoValidate, deletePermanent, CancellationToken.None);

    /// <summary>
    /// As <see cref="ApplyOperationRange(EntityOperation, ICollection{TEntity}, bool, bool)"/>, with the
    /// token passed through to <c>SaveChangesAsync</c>.
    /// </summary>
    /// <remarks>
    /// Every read on this repository has taken a token since the first version; the write path did
    /// not, so a cancelled request still wrote. The token is required here rather than optional so
    /// that the four-argument call keeps binding to the overload above instead of becoming ambiguous.
    /// </remarks>
    public async Task<ICollection<TEntity>> ApplyOperationRange(EntityOperation entityOperation, ICollection<TEntity> entities, bool alsoValidate, bool deletePermanent, CancellationToken cancellationToken)
    {
        var currentUser = GetCurrentUser();
        foreach (var entity in entities)
        {
            CheckRoleRequirement(entityOperation, entity);
            await entity.CheckEntityDataForAsync(entityOperation, alsoValidate, deletePermanent, this, currentUser);
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
        await _context.SaveChangesAsync(cancellationToken);
        return entities;
    }

    public Task<TEntity> ApplyOperation(EntityOperation entityOperation, TEntity entity, bool alsoValidate, bool deletePermanent = false)
        => ApplyOperation(entityOperation, entity, alsoValidate, deletePermanent, CancellationToken.None);

    /// <summary>
    /// As <see cref="ApplyOperation(EntityOperation, TEntity, bool, bool)"/>, with the token passed
    /// through to <c>SaveChangesAsync</c>.
    /// </summary>
    public async Task<TEntity> ApplyOperation(EntityOperation entityOperation, TEntity entity, bool alsoValidate, bool deletePermanent, CancellationToken cancellationToken)
    {
        CheckRoleRequirement(entityOperation, entity);
        await entity.CheckEntityDataForAsync(entityOperation, alsoValidate, deletePermanent, this, GetCurrentUser());
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
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public Task<PaginateResponseModel<TEntity>> GetListAsync(IQueryable<TEntity> query, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null, Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null, int pageNumber = 0, int itemsPerPage = 0, bool enableTracking = true, bool includeDeletedRecords = false, CancellationToken cancellationToken = default)
    {
        var q = updateQuery(query, null, include, enableTracking, includeDeletedRecords);
        if (orderBy != null)
            return orderBy(q).ToPaginateAsync(pageNumber, itemsPerPage, cancellationToken);
        else
            return q.ToPaginateAsync(pageNumber, itemsPerPage, cancellationToken);

    }

    #endregion

    #region Restore (Soft Delete)

    /// <summary>
    /// Brings a soft-deleted row back.
    /// </summary>
    /// <remarks>
    /// <para><b>Gated by the roles required for <see cref="EntityOperation.Delete"/>, not Update.</b>
    /// Restoring is the inverse of deleting, so it must not be the weaker gate of the two: if only an
    /// Owner may delete a row, an Editor must not be able to bring it back.</para>
    /// <para>Validation is <b>opt-in</b>: pass <paramref name="alsoValidate"/> to run the entity's
    /// <c>CheckDataForAsync</c> first. It is not the default because the row is loaded without any
    /// <c>include</c>, so an entity that validates its children — "an invoice must have at least two
    /// lines" — would fail every restore.</para>
    /// </remarks>
    public Task<TEntity> RestoreAsync(TEntityId id, CancellationToken cancellationToken = default)
        => RestoreAsync(id, alsoValidate: false, cancellationToken);

    /// <inheritdoc cref="IAsyncRepository{TEntity, TEntityId}.RestoreAsync(TEntityId, bool, CancellationToken)"/>
    public async Task<TEntity> RestoreAsync(TEntityId id, bool alsoValidate, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdAsync(id, includeDeletedRecords: true, cancellationToken: cancellationToken)
            ?? throw new KeyNotFoundException($"Entity of type '{typeof(TEntity).Name}' with id '{id}' not found.");

        // Authorisation first, before anything observable happens — including the early return below,
        // which would otherwise tell an unauthorised caller whether the row is deleted.
        CheckRoleRequirement(EntityOperation.Delete, entity);

        if (entity is not IEntityHasSoftDeleteFeature softDeleteEntity)
            throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' does not support soft delete.");

        if (!softDeleteEntity.IsDeleted)
            return entity;

        // Validate before mutating: a failure here must leave the tracked entity untouched, or a
        // later SaveChanges somewhere else would persist a half-restored row.
        //
        // As Update, not Delete: the row is being written. Running it as Delete would trigger rules
        // like "cannot delete a category that has products" — the opposite of what is happening.
        if (alsoValidate && entity is IEntityHasCheckDataFor<TEntity, TEntityId> validatable)
            await validatable.CheckDataForAsync(EntityOperation.Update, alsoValidate: true, this);

        softDeleteEntity.IsDeleted = false;

        if (entity is IEntityHasDeletedDate deletedDateEntity)
            deletedDateEntity.DeletedDateUTC = null;

        if (entity is IEntityHasDeletedBy deletedByEntity)
            deletedByEntity.DeletedBy = null;

        if (entity is IEntityHasUpdatedDate updatedDateEntity)
            updatedDateEntity.UpdatedDateUTC = DateTime.UtcNow;

        if (entity is IEntityHasUpdatedBy updatedByEntity)
        {
            var currentUser = GetCurrentUser();
            if (currentUser != null)
                updatedByEntity.UpdatedBy = currentUser;
        }

        _context.Update(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    #endregion

    #region Transaction Support

    /// <summary>
    /// The transaction this repository started, or the one already running on its <c>DbContext</c>.
    /// </summary>
    /// <remarks>
    /// A transaction belongs to the <c>DbContext</c>, not to the repository. Several repositories
    /// sharing one context — the unit-of-work arrangement — therefore share one transaction, and only
    /// the repository that began it can commit or roll it back. The others still write inside it,
    /// because their <c>SaveChanges</c> goes through the same context.
    /// </remarks>
    public IDbContextTransaction? CurrentTransaction => _transaction ?? _context.Database.CurrentTransaction;

    /// <summary>True when a transaction is active on this repository's DbContext, whoever started it.</summary>
    public bool HasActiveTransaction => CurrentTransaction != null;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
            throw new InvalidOperationException("A transaction is already in progress. Commit or rollback the current transaction before starting a new one.");

        // Started by someone else on the same DbContext: another repository sharing it, or the caller
        // going through context.Database directly. EF's own error for this names neither cause, and
        // the shared-context case is the whole point of a unit of work, so it is worth naming here.
        if (_context.Database.CurrentTransaction != null)
            throw new InvalidOperationException(
                $"A transaction is already active on the DbContext this '{typeof(TEntity).Name}' repository uses, " +
                "started by another repository sharing it or by the caller. Begin and commit on one repository " +
                "only — repositories on the same context already write inside the same transaction.");

        _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException(NoTransactionMessage("commit"));

        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction == null)
            throw new InvalidOperationException(NoTransactionMessage("roll back"));

        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    private string NoTransactionMessage(string verb) =>
        _context.Database.CurrentTransaction != null
            ? $"This repository did not start the transaction that is running on its DbContext, so it cannot {verb} it. " +
              "Call the method on the repository that began it, or on context.Database directly."
            : "No transaction in progress. Call BeginTransactionAsync first.";

    #endregion
}
