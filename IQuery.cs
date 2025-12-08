namespace FBC.Repository;

public interface IQuery<T>
{
    IQueryable<T> GetQueryable();
}

