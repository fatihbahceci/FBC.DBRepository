using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace FBC.DBRepository;

public interface IAsyncRepository<TEntity, TEntityId> : IQuery<TEntity>
    where TEntity : Entity<TEntityId, TEntity>
    where TEntityId : IEquatable<TEntityId>

{

    /// <summary>
    /// <see cref="IQuery{T}.GetQueryable"/> with the soft-delete filter already applied.
    /// </summary>
    /// <remarks>
    /// <para>For the case the raw queryable is usually wanted for — a query the repository methods
    /// cannot express — without the part that is usually forgotten. On an entity that does not
    /// implement <see cref="IEntityHasSoftDeleteFeature"/> this is the same as
    /// <see cref="IQuery{T}.GetQueryable"/>.</para>
    /// <para>Declared with a default body so that adding it in 0.5.0 does not break a hand-written
    /// implementation of this interface.</para>
    /// </remarks>
    IQueryable<TEntity> GetActiveQueryable()
    {
        var queryable = GetQueryable();

        return typeof(IEntityHasSoftDeleteFeature).IsAssignableFrom(typeof(TEntity))
            ? queryable.Where(x => !((IEntityHasSoftDeleteFeature)x).IsDeleted)
            : queryable;
    }

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
    /// <param name="itemsPerPage">The number of items per page. Must be greater than or equal to 0. If 0, every matching item is
    /// returned in one page and <paramref name="pageNumber"/> is ignored.
    /// <para><b>Nothing caps this value.</b> A page size that arrives from a request and reaches this
    /// method unclamped reads the whole table into memory on one call. Clamp it where it enters your
    /// application — <c>Math.Clamp(size, 1, MaxSize)</c> — and do not let 0 through from outside.</para></param>
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
        int pageNumber = 0,
        int itemsPerPage = 0,
        bool enableTracking = true,
        bool includeDeletedRecords = false,
        CancellationToken cancellationToken = default
    );
    Task<PaginateResponseModel<TEntity>> GetListAsync(
       IQueryable<TEntity> query,
       Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
       Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
       int pageNumber = 0,
       int itemsPerPage = 0,
       bool enableTracking = true,
       bool includeDeletedRecords = false,
       CancellationToken cancellationToken = default
   );

    Task<TEntity?> GetByIdAsync(
        TEntityId id,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        bool enableTracking = true,
        bool includeDeletedRecords = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves multiple entities by their primary keys in a single query.
    /// </summary>
    Task<IList<TEntity>> GetByIdsAsync(
        IEnumerable<TEntityId> ids,
        Func<IQueryable<TEntity>, IIncludableQueryable<TEntity, object>>? include = null,
        bool enableTracking = true,
        bool includeDeletedRecords = false,
        CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(
      Expression<Func<TEntity, bool>>? predicate = null,
      bool enableTracking = true,
      bool includeDeletedRecords = false,
      CancellationToken cancellationToken = default
    );

    Task<int> CountAsync(
      Expression<Func<TEntity, bool>>? predicate = null,
      bool includeDeletedRecords = false,
      CancellationToken cancellationToken = default
    );

    Task<TEntity> ApplyOperation(EntityOperation operationType, TEntity entity, bool alsoValidate, bool deletePermanent = false);
    Task<ICollection<TEntity>> ApplyOperationRange(EntityOperation operationType, ICollection<TEntity> entities, bool alsoValidate, bool deletePermanent = false);

    /// <summary>
    /// As <see cref="ApplyOperation(EntityOperation, TEntity, bool, bool)"/>, with a cancellation token.
    /// </summary>
    /// <remarks>
    /// <para>The token is <b>required</b>, not optional: an optional one would make the four-argument
    /// call ambiguous between the two overloads and stop existing code from compiling.</para>
    /// <para>Declared with a default body so that adding it in 0.5.0 does not break a hand-written
    /// implementation of this interface. That body ignores the token — it can only forward to the
    /// overload above, which never had one. <see cref="EFRepositoryBase{TEntity, TEntityId, TContext}"/>
    /// overrides it and honours the token properly.</para>
    /// </remarks>
    Task<TEntity> ApplyOperation(EntityOperation operationType, TEntity entity, bool alsoValidate, bool deletePermanent, CancellationToken cancellationToken)
        => ApplyOperation(operationType, entity, alsoValidate, deletePermanent);

    /// <summary>
    /// As <see cref="ApplyOperationRange(EntityOperation, ICollection{TEntity}, bool, bool)"/>, with a
    /// cancellation token. See the remarks on the single-entity overload.
    /// </summary>
    Task<ICollection<TEntity>> ApplyOperationRange(EntityOperation operationType, ICollection<TEntity> entities, bool alsoValidate, bool deletePermanent, CancellationToken cancellationToken)
        => ApplyOperationRange(operationType, entities, alsoValidate, deletePermanent);

    /// <summary>
    /// Restores a soft-deleted entity by setting IsDeleted to false.
    /// Only works on entities implementing IEntityHasSoftDeleteFeature.
    /// </summary>
    /// <remarks>
    /// Since 0.5.0 this checks the roles required for <see cref="EntityOperation.Delete"/>. It does
    /// <b>not</b> validate — see the overload below for why that is opt-in.
    /// </remarks>
    Task<TEntity> RestoreAsync(TEntityId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a soft-deleted entity, optionally running the entity's own validation first.
    /// </summary>
    /// <remarks>
    /// <para>A row can stop being valid while it sits deleted — the ordinary case is a unique value
    /// another row has taken meanwhile. With <paramref name="alsoValidate"/> the entity's
    /// <c>CheckDataForAsync</c> runs as <see cref="EntityOperation.Update"/> before anything is
    /// changed, so the entity's own message is raised instead of a database constraint error, and a
    /// refused restore leaves nothing half-changed.</para>
    /// <para><b>Why it is opt-in rather than the default.</b> The row is loaded without any
    /// <c>include</c>, so its child collections are empty. An entity whose validation covers its
    /// children — "an invoice must have at least two lines" — would fail every restore. Turning this
    /// on by default broke exactly that case in an application using this library, which is how the
    /// rule was found. Pass true when the entity validates only its own columns.</para>
    /// <para>Declared with a default body, so adding it in 0.5.0 does not break a hand-written
    /// implementation of this interface; that body ignores the flag.</para>
    /// </remarks>
    Task<TEntity> RestoreAsync(TEntityId id, bool alsoValidate, CancellationToken cancellationToken = default)
        => RestoreAsync(id, cancellationToken);

    /// <summary>
    /// Begins a database transaction. Use with CommitTransactionAsync/RollbackTransactionAsync
    /// to wrap multiple operations in a single atomic unit.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the current transaction started by BeginTransactionAsync.
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the current transaction started by BeginTransactionAsync.
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
