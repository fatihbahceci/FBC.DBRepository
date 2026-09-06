namespace FBC.DBRepository;

/// <summary>
/// The exception an entity throws from <see cref="IEntityHasCheckDataFor{TEntity, TId}.CheckDataForAsync"/>
/// when the data is not valid.
/// </summary>
/// <remarks>
/// <para>The validation contract asked entities to throw without saying what, so every application
/// invented its own type — and every endpoint's <c>catch</c> then caught a different one. That makes
/// the boundary between "the caller sent something invalid" (400) and "something went wrong" (500)
/// a per-project decision rather than a library one.</para>
/// <para><b>Nothing in this library throws it</b>, and nothing requires it: an entity may keep
/// throwing whatever it already throws. It exists so that new code has a shared type to agree on.</para>
/// <para>The name is not <c>ValidationException</c> on purpose. That name is taken by
/// <c>System.ComponentModel.DataAnnotations</c>, and <see cref="Entity{TId, TEntity}"/> already brings
/// that namespace in for <c>[Key]</c>. A second <c>ValidationException</c> in this namespace would
/// make the name ambiguous — CS0104 — in any file that imports both, and existing code would stop
/// compiling.</para>
/// <example>
/// <code>
/// public async Task CheckDataForAsync(EntityOperation operation, bool alsoValidate, IAsyncRepository&lt;Product, int&gt; repository)
/// {
///     Name = Name.Trim();
///
///     if (!alsoValidate) return;
///
///     var (name, selfId) = (Name, Id);
///     if (await repository.AnyAsync(p =&gt; p.Name == name &amp;&amp; p.Id != selfId))
///         throw new EntityValidationException($"A product named '{name}' already exists.");
/// }
/// </code>
/// </example>
/// </remarks>
public class EntityValidationException : Exception
{
    public EntityValidationException(string message) : base(message) { }

    public EntityValidationException(string message, Exception innerException)
        : base(message, innerException) { }
}
