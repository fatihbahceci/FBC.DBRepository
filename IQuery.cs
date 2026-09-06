namespace FBC.DBRepository;

/// <summary>
/// Raw queryable access, kept separate from the rest of the repository contract.
/// </summary>
/// <remarks>
/// <see cref="IAsyncRepository{TEntity, TEntityId}"/> derives from this, so every repository already
/// offers <see cref="GetQueryable"/>; this interface is where that member is declared.
/// </remarks>
public interface IQuery<T>
{
    /// <summary>
    /// The entity set as an <see cref="IQueryable{T}"/>, for a query the repository methods cannot express.
    /// </summary>
    /// <remarks>
    /// <para><b>Soft delete is not applied here.</b> This library filters deleted rows inside the query
    /// methods rather than through a global query filter, so what comes back from this method still
    /// contains them — which is the point when you want them, and a silent wrong answer when you do
    /// not.</para>
    /// <para>Three ways out, in order of preference:</para>
    /// <list type="bullet">
    /// <item><description>Use <c>GetAsync</c> / <c>GetListAsync</c> / <c>AnyAsync</c> / <c>CountAsync</c>,
    /// which filter for you.</description></item>
    /// <item><description>Call <c>GetActiveQueryable()</c>, which is this queryable with the filter
    /// already applied.</description></item>
    /// <item><description>Hand this queryable to <c>GetListAsync(query, …)</c>, which applies the filter
    /// on the way through.</description></item>
    /// </list>
    /// <para>Writing <c>Where(x =&gt; !x.IsDeleted)</c> by hand works too, and is what several projects
    /// ended up doing — the point of the alternatives above is that nobody has to remember.</para>
    /// </remarks>
    IQueryable<T> GetQueryable();
}
