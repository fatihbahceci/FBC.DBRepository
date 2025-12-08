using Microsoft.EntityFrameworkCore;

namespace FBC.Repository;

public static class IQueryablePaginateExtensions
{
    public static async Task<PaginateResponseModel<T>> ToPaginateAsync<T>(this IQueryable<T> source, int index, int size, CancellationToken cancellationToken = default)
    {
        int count = await source.CountAsync(cancellationToken).ConfigureAwait(false);

        List<T> items = await source.Skip(index * size).Take(size).ToListAsync(cancellationToken).ConfigureAwait(false);

        PaginateResponseModel<T> list = new()
        {
            Size = size,
            Index = index,
            Count = count,
            Pages = (int)Math.Ceiling(count / (double)size),
            Items = items,
        };
        return list;
    }

    public static PaginateResponseModel<T> ToPaginatec<T>(this IQueryable<T> source, int index, int size)
    {
        int count = source.Count();

        List<T> items = source.Skip(index * size).Take(size).ToList();

        PaginateResponseModel<T> list = new()
        {
            Size = size,
            Index = index,
            Count = count,
            Pages = (int)Math.Ceiling(count / (double)size),
            Items = items,
        };
        return list;
    }
}

