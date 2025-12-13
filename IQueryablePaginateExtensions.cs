using Microsoft.EntityFrameworkCore;

namespace FBC.DBRepository;

public static class IQueryablePaginateExtensions
{
    /// <summary>
    /// Asynchronously creates a paginated response from the specified queryable source.
    /// </summary>
    /// <remarks>If itemsPerPage is 0, all items from the source are returned in a single page. The method
    /// uses deferred execution and executes the query against the underlying data source when awaited.</remarks>
    /// <typeparam name="T">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">The queryable data source to paginate.</param>
    /// <param name="pageNumber">The zero-based index of the page to retrieve. Must be greater than or equal to 0.</param>
    /// <param name="itemsPerPage">The number of items to include in each page. Must be greater than or equal to 0. If 0, all items are returned after skipping to the specified page.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a PaginateResponseModel<T> with the
    /// items for the specified page and pagination metadata.</returns>
    public static async Task<PaginateResponseModel<T>> ToPaginateAsync<T>(this IQueryable<T> source, int pageNumber, int itemsPerPage, CancellationToken cancellationToken = default)
    {
        int count = await source.CountAsync(cancellationToken).ConfigureAwait(false);

        List<T> items = itemsPerPage == 0
            ? pageNumber == 0
                ? await source.ToListAsync(cancellationToken).ConfigureAwait(false)
                : await source.Skip(pageNumber * itemsPerPage).ToListAsync(cancellationToken).ConfigureAwait(false)
            : await source.Skip(pageNumber * itemsPerPage).Take(itemsPerPage).ToListAsync(cancellationToken).ConfigureAwait(false);

        PaginateResponseModel<T> list = new()
        {
            Size = itemsPerPage,
            Index = pageNumber,
            Count = count,
            Pages = (int)Math.Ceiling(count / (double)itemsPerPage),
            Items = items,
        };
        return list;
    }

    //public static PaginateResponseModel<T> ToPaginatec<T>(this IQueryable<T> source, int index, int size)
    //{
    //    int count = source.Count();

    //    List<T> items = source.Skip(index * size).Take(size).ToList();

    //    PaginateResponseModel<T> list = new()
    //    {
    //        Size = size,
    //        Index = index,
    //        Count = count,
    //        Pages = (int)Math.Ceiling(count / (double)size),
    //        Items = items,
    //    };
    //    return list;
    //}
}

