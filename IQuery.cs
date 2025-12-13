namespace FBC.DBRepository;

public interface IQuery<T>
{
    IQueryable<T> GetQueryable();
}

