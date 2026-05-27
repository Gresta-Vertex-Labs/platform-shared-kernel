namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Convenience base class for read-only query specifications.
/// Automatically applies <c>AsNoTracking = true</c> so that consuming repositories
/// suppress change-tracking on every query built from this specification.
/// </summary>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
/// <remarks>
/// <para>
/// Use <see cref="ReadOnlySpecification{T}"/> instead of calling <c>ApplyNoTracking()</c>
/// manually in every read-only specification constructor. Any subclass constructor inherits
/// the no-tracking flag automatically.
/// </para>
/// <para>
/// This class is <c>abstract</c> — it cannot be instantiated directly, only subclassed.
/// </para>
/// <example>
/// <code>
/// public sealed class ActiveOrdersReadOnlySpec : ReadOnlySpecification&lt;Order&gt;
/// {
///     public ActiveOrdersReadOnlySpec()
///     {
///         AddCriteria(o => !o.IsDeleted);
///         ApplyOrderByDescending(o => o.CreatedOn);
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public abstract class ReadOnlySpecification<T> : Specification<T>
{
    /// <summary>
    /// Initialises the specification with <c>AsNoTracking = true</c>.
    /// </summary>
    protected ReadOnlySpecification() => ApplyNoTracking();
}
