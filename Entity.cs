using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FBC.DBRepository;

public enum EntityOperation
{
    Create,
    Update,
    Delete
}
public interface IEntityHasSoftDeleteFeature
{
    public bool IsDeleted { get; set; }
}
public interface IEntityHasCreatedDate
{
    public DateTime CreatedDateUTC { get; set; }
}
public interface IEntityHasUpdatedDate
{
    public DateTime? UpdatedDateUTC { get; set; }
}
public interface IEntityHasDeletedDate
{
    public DateTime? DeletedDateUTC { get; set; }
}

public interface IEntityHasCreatedBy
{
    public string? CreatedBy { get; set; }
}
public interface IEntityHasUpdatedBy
{
    public string? UpdatedBy { get; set; }
}
public interface IEntityHasDeletedBy
{
    public string? DeletedBy { get; set; }
}

public interface IEntityHasCheckDataFor<TEntity, TId>
    where TId : IEquatable<TId>
    where TEntity : Entity<TId, TEntity>
{
    /// <summary>
    /// Tweak, adjust and validate data before create, update, delete operation
    /// </summary>
    /// <param name="operation"></param>
    /// <param name="alsoValidate"></param>
    /// <param name="query"></param>
    Task CheckDataForAsync(EntityOperation operation, bool alsoValidate, IQueryable<TEntity> query);
}
public abstract class Entity<TId, TEntity>
    where TId : IEquatable<TId>
    where TEntity : Entity<TId, TEntity>
{
    [Key]
    public TId Id { get; set; }


    public Entity()
    {
        Id = default!;
        if (this is IEntityHasCreatedDate entityWithCreatedDate)
            entityWithCreatedDate.CreatedDateUTC = DateTime.UtcNow;
    }

    public Entity(TId id)
    {
        Id = id;
        if (this is IEntityHasCreatedDate entityWithCreatedDate)
            entityWithCreatedDate.CreatedDateUTC = DateTime.UtcNow;
    }
    /// <summary>
    /// Performs entity data checks and updates entity metadata based on the specified operation type.
    /// </summary>
    /// <remarks>This method updates entity metadata such as creation, update, or deletion timestamps and
    /// soft-delete flags, depending on the operation type and implemented interfaces. For permanent deletions, only
    /// data checks are performed before the entity is deleted.</remarks>
    /// <param name="operationType">The type of entity operation being performed, such as create, update, or delete. Determines which checks and
    /// metadata updates are applied.</param>
    /// <param name="alsoValidate">true to perform additional validation during the data check; otherwise, false.</param>
    /// <param name="isDeletingPermanently">true if the entity is being permanently deleted; otherwise, false. When true, only data checks are performed
    /// before deletion.</param>
    /// <param name="GetQueryable">A function that returns an IQueryable of the entity type, used to provide the data context for validation and
    /// checks.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    internal async Task CheckEntityDataForAsync(EntityOperation operationType, bool alsoValidate, bool isDeletingPermanently, Func<IQueryable<TEntity>> GetQueryable, string? currentUser = null)
    {
        if (this is IEntityHasCheckDataFor<TEntity, TId> entityWithCheckData)
            await entityWithCheckData.CheckDataForAsync(operationType, alsoValidate, GetQueryable());

        if (isDeletingPermanently && operationType == EntityOperation.Delete)
            return;

        switch (operationType)
        {
            case EntityOperation.Create:
                if (this is IEntityHasCreatedDate entity_c)
                    entity_c.CreatedDateUTC = DateTime.UtcNow;
                if (this is IEntityHasCreatedBy entity_cb && currentUser != null)
                    entity_cb.CreatedBy = currentUser;
                break;
            case EntityOperation.Update:
                if (this is IEntityHasUpdatedDate entity_u)
                    entity_u.UpdatedDateUTC = DateTime.UtcNow;
                if (this is IEntityHasUpdatedBy entity_ub && currentUser != null)
                    entity_ub.UpdatedBy = currentUser;
                break;
            case EntityOperation.Delete:
                if (this is IEntityHasSoftDeleteFeature entity_sd)
                    entity_sd.IsDeleted = true;
                if (this is IEntityHasDeletedDate entity_d)
                    entity_d.DeletedDateUTC = DateTime.UtcNow;
                if (this is IEntityHasDeletedBy entity_db && currentUser != null)
                    entity_db.DeletedBy = currentUser;
                break;
        }
    }
}

