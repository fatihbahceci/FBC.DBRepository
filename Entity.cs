using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FBC.Repository;

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

    void CheckDataFor(EntityOperation operation, bool alsoValidate, IQueryable<TEntity> query);
}
//TODO:
//public string? CreatedBy { get; set; }
//public string? UpdatedBy { get; set; }
//public string? DeletedBy { get; set; }
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

    internal async Task CheckEntityDataForAsync(EntityOperation operationType, bool alsoValidate, bool isDeletingPermamently, Func<IQueryable<TEntity>> GetQueryable)
    {
        //Even for permanent delete, we need to check data first before deleting
        if (this is IEntityHasCheckDataFor<TEntity, TId> entityWithCheckData)
            entityWithCheckData.CheckDataFor(operationType, alsoValidate, GetQueryable());

        if (isDeletingPermamently && operationType == EntityOperation.Delete)
            return;
        switch (operationType)
        {
            case EntityOperation.Create:
                if (this is IEntityHasCreatedDate entity_c)
                {
                    entity_c.CreatedDateUTC = DateTime.UtcNow;
                }
                break;
            case EntityOperation.Update:
                if (this is IEntityHasUpdatedDate entity_u)
                {
                    entity_u.UpdatedDateUTC = DateTime.UtcNow;
                }
                break;
            case EntityOperation.Delete:
                if (this is IEntityHasSoftDeleteFeature entity_sd)
                {
                    entity_sd.IsDeleted = true;
                }
                if (this is IEntityHasDeletedDate entity_d)
                {
                    entity_d.DeletedDateUTC = DateTime.UtcNow;
                }
                break;
        }
    }
}

