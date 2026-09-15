namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Base class for a query whose results are only read, never modified: the evaluator runs it without change
/// tracking.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Usage.</b> Derive from it for list, lookup and report queries instead of calling
/// <see cref="Specification{T}.ApplyNoTracking"/> in each constructor. The flag is set before the subclass
/// constructor runs and cannot be cleared.
/// </para>
/// <para>
/// <b>Pitfall.</b> Never use it to load entities you intend to modify: changes to untracked entities are not
/// saved. Use <see cref="Specification{T}"/> for write-side fetches.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class RecentOrdersSpec : ReadOnlySpecification&lt;Order&gt;
/// {
///     public RecentOrdersSpec(DateTimeOffset since)
///     {
///         AddCriteria(o =&gt; o.CreatedOn &gt;= since);
///         ApplyOrderByDescending(o =&gt; o.CreatedOn);
///     }
/// }
/// </code>
/// </example>
public abstract class ReadOnlySpecification<T> : Specification<T>
{
    /// <summary>
    /// Initializes a new read-only specification with <see cref="Specification{T}.AsNoTracking"/> set to
    /// <see langword="true"/>.
    /// </summary>
    protected ReadOnlySpecification() => ApplyNoTracking();
}
