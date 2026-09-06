using Microsoft.EntityFrameworkCore;

namespace FBC.DBRepository;

public static class IQueryablePaginateExtensions
{
    /// <summary>
    /// Asynchronously creates a paginated response from the specified queryable source.
    /// </summary>
    /// <remarks>
    /// <para>If <paramref name="itemsPerPage"/> is 0 the whole source is returned as a single page and
    /// <paramref name="pageNumber"/> is ignored. Earlier versions documented "all items are returned
    /// after skipping to the specified page", which no code could have produced: the skip was
    /// <c>pageNumber * itemsPerPage</c>, and with a page size of zero that is always zero.</para>
    /// <para><b>An unbounded page size reads the whole table into memory.</b> Nothing here caps it,
    /// because the cap belongs to the caller who knows the table: clamp the value that reaches this
    /// method (<c>Math.Clamp(size, 1, MaxSize)</c>) rather than passing a page size through from a
    /// request.</para>
    /// <para>Execution is deferred; the query runs when awaited.</para>
    /// </remarks>
    /// <typeparam name="T">The type of the elements in the source sequence.</typeparam>
    /// <param name="source">The queryable data source to paginate.</param>
    /// <param name="pageNumber">The zero-based index of the page to retrieve. Must be greater than or equal to 0. Ignored when <paramref name="itemsPerPage"/> is 0.</param>
    /// <param name="itemsPerPage">The number of items per page. Must be greater than or equal to 0. If 0, every matching item is returned in one page.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains a PaginateResponseModel<T> with the
    /// items for the specified page and pagination metadata.</returns>
    public static async Task<PaginateResponseModel<T>> ToPaginateAsync<T>(this IQueryable<T> source, int pageNumber, int itemsPerPage, CancellationToken cancellationToken = default)
    {
        int count = await source.CountAsync(cancellationToken).ConfigureAwait(false);

        // No paging: one page holding everything. The branch that used to sit here skipped
        // pageNumber * itemsPerPage rows, which is zero whenever itemsPerPage is zero — it did exactly
        // what this line does and only made the reader look for a difference.
        List<T> items = itemsPerPage == 0
            ? await source.ToListAsync(cancellationToken).ConfigureAwait(false)
            : await source.Skip(pageNumber * itemsPerPage).Take(itemsPerPage).ToListAsync(cancellationToken).ConfigureAwait(false);

        PaginateResponseModel<T> list = new()
        {
            ItemsPerPage = itemsPerPage,
            PageIndex = pageNumber,
            TotalFilteredCount = count,
            TotalPages = itemsPerPage == 0 ? 1 :
            (int)Math.Ceiling(count / (double)itemsPerPage),
            Items = items,
        };
        return list;
    }
}

