using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// A fluent builder for a query specification, created by <see cref="Spec.For{T}"/>; it is itself the
/// specification, so pass it to a repository directly.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Rules.</b> The same as <see cref="Specification{T}"/>: <see cref="Where"/> accumulates with AND, one
/// primary sort (a second <see cref="OrderBy{TKey}"/> throws), <see cref="ThenBy{TKey}"/> requires a primary
/// sort, and <c>ThenInclude</c> is checked by the compiler, including through collections.
/// </para>
/// <para>
/// <b>Mutability.</b> Each method changes this builder and returns it. Build a specification once and do not
/// change it after it has been handed to a repository.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var spec = Spec.For&lt;Order&gt;()
///     .Where(o =&gt; o.CustomerId == customerId)
///     .Include(o =&gt; o.Lines).ThenInclude(l =&gt; l.Product)
///     .OrderByDescending(o =&gt; o.CreatedOn)
///     .ThenBy(o =&gt; o.Id)
///     .AsSplitQuery();
///
/// PagedList&lt;Order&gt; page = await orders.ListPagedAsync(spec, pageRequest, ct);
/// </code>
/// </example>
public class SpecificationBuilder<T> : ISpecification<T>
{
    private protected SpecificationBuilder(Specification<T> target) => Target = target;

    /// <summary>Gets the specification this builder writes to.</summary>
    internal Specification<T> Target { get; }

    Expression<Func<T, bool>>? ISpecification<T>.Criteria => Target.Criteria;

    IReadOnlyList<Expression<Func<T, object>>> ISpecification<T>.Includes => Target.Includes;

    Expression<Func<T, object>>? ISpecification<T>.OrderBy => Target.OrderBy;

    Expression<Func<T, object>>? ISpecification<T>.OrderByDescending => Target.OrderByDescending;

    IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)> ISpecification<T>.ThenBys =>
        Target.ThenBys;

    int? ISpecification<T>.Skip => Target.Skip;

    int? ISpecification<T>.Take => Target.Take;

    bool ISpecification<T>.IsDistinct => Target.IsDistinct;

    bool ISpecification<T>.AsSplitQuery => Target.AsSplitQuery;

    bool ISpecification<T>.IncludeDeleted => Target.IncludeDeleted;

    IReadOnlyList<string> ISpecification<T>.StringIncludes => Target.StringIncludes;

    /// <summary>Adds a filter condition, combined by logical AND with the criteria already present.</summary>
    /// <param name="criteria">The condition an entity must also satisfy. Must not be <see langword="null"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="criteria"/> is <see langword="null"/>.</exception>
    public SpecificationBuilder<T> Where(Expression<Func<T, bool>> criteria)
    {
        Target.AddCriteriaCore(criteria);
        return this;
    }

    /// <summary>Adds a navigation to eager-load; continue the path with <c>ThenInclude</c>.</summary>
    /// <typeparam name="TProperty">The navigation's type: an entity or a collection of entities.</typeparam>
    /// <param name="navigation">
    /// The navigation selector, such as <c>o =&gt; o.Lines</c>, or a filtered include. Must not be
    /// <see langword="null"/>.
    /// </param>
    /// <returns>A builder that continues the include path and exposes every other builder method.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="navigation"/> is <see langword="null"/>.</exception>
    public IncludableSpecificationBuilder<T, TProperty> Include<TProperty>(Expression<Func<T, TProperty>> navigation) =>
        Target.AddIncludeCore(navigation);

    /// <summary>Adds a dot-separated navigation path to eager-load, such as <c>"Lines.Product"</c>.</summary>
    /// <param name="path">The navigation path. Must not be <see langword="null"/>, empty or whitespace.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or whitespace.</exception>
    public SpecificationBuilder<T> Include(string path)
    {
        Target.AddStringIncludeCore(path);
        return this;
    }

    /// <summary>Sets the ascending primary sort.</summary>
    /// <typeparam name="TKey">The sort key type.</typeparam>
    /// <param name="keySelector">The sort key selector. Must not be <see langword="null"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">A primary sort is already set.</exception>
    public SpecificationBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        Target.SetOrderCore(SpecificationExpressions.ToObjectSelector(keySelector), descending: false);
        return this;
    }

    /// <summary>Sets the descending primary sort.</summary>
    /// <typeparam name="TKey">The sort key type.</typeparam>
    /// <param name="keySelector">The sort key selector. Must not be <see langword="null"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">A primary sort is already set.</exception>
    public SpecificationBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        Target.SetOrderCore(SpecificationExpressions.ToObjectSelector(keySelector), descending: true);
        return this;
    }

    /// <summary>Adds an ascending secondary sort key.</summary>
    /// <typeparam name="TKey">The sort key type.</typeparam>
    /// <param name="keySelector">The sort key selector. Must not be <see langword="null"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">No primary sort is set yet.</exception>
    public SpecificationBuilder<T> ThenBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        Target.AddThenByCore(SpecificationExpressions.ToObjectSelector(keySelector), descending: false);
        return this;
    }

    /// <summary>Adds a descending secondary sort key.</summary>
    /// <typeparam name="TKey">The sort key type.</typeparam>
    /// <param name="keySelector">The sort key selector. Must not be <see langword="null"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="InvalidOperationException">No primary sort is set yet.</exception>
    public SpecificationBuilder<T> ThenByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        Target.AddThenByCore(SpecificationExpressions.ToObjectSelector(keySelector), descending: true);
        return this;
    }

    /// <summary>Skips a fixed number of ordered rows. For page-by-page access use a page request instead.</summary>
    /// <param name="count">The number of rows to skip. Must be zero or greater.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public SpecificationBuilder<T> Skip(int count)
    {
        Target.SetSkipCore(count);
        return this;
    }

    /// <summary>Limits the query to a fixed number of ordered rows, such as "the ten most recent".</summary>
    /// <param name="count">The maximum number of rows. Must be at least 1.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is less than 1.</exception>
    public SpecificationBuilder<T> Take(int count)
    {
        Target.SetTakeCore(count);
        return this;
    }

    /// <summary>Removes duplicate rows from the result.</summary>
    /// <returns>This builder.</returns>
    public SpecificationBuilder<T> Distinct()
    {
        Target.SetDistinctCore();
        return this;
    }

    /// <summary>Loads each included collection with its own query instead of one joined query.</summary>
    /// <returns>This builder.</returns>
    public SpecificationBuilder<T> AsSplitQuery()
    {
        Target.SetSplitQueryCore();
        return this;
    }

    /// <summary>Returns soft-deleted entities as well; tenant isolation stays in force.</summary>
    /// <returns>This builder.</returns>
    public SpecificationBuilder<T> IncludeDeleted()
    {
        Target.SetIncludeDeletedCore();
        return this;
    }

    /// <summary>
    /// Projects each matching entity to <typeparamref name="TResult"/>, turning this query into a projection
    /// specification.
    /// </summary>
    /// <typeparam name="TResult">The projected type, typically a DTO.</typeparam>
    /// <param name="selector">
    /// The projection, translated to SQL by the persistence layer. Must not be <see langword="null"/>.
    /// </param>
    /// <returns>A projection specification that shares this builder's filter, includes and ordering.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is <see langword="null"/>.</exception>
    public IProjectionSpecification<T, TResult> Select<TResult>(Expression<Func<T, TResult>> selector) =>
        new SelectedSpecification<T, TResult>(Target, selector);

    /// <summary>Returns whether <paramref name="entity"/> satisfies the criteria, evaluated in memory.</summary>
    /// <param name="entity">The entity to test.</param>
    /// <returns><see langword="true"/> when it matches or there are no criteria.</returns>
    public bool IsSatisfiedBy(T entity) => Target.IsSatisfiedBy(entity);
}

/// <summary>
/// A <see cref="SpecificationBuilder{T}"/> positioned on an included navigation, so <c>ThenInclude</c> can
/// continue the path; every other builder method is available too.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <typeparam name="TProperty">The type of the navigation the path currently ends at.</typeparam>
/// <remarks>
/// The continued path (<c>"Lines.Product"</c>) is recorded in <see cref="ISpecification{T}.StringIncludes"/>.
/// </remarks>
public sealed class IncludableSpecificationBuilder<T, TProperty> : SpecificationBuilder<T>, IIncludableSpecificationBuilder<T, TProperty>
{
    internal IncludableSpecificationBuilder(Specification<T> target, string? path)
        : base(target) => Path = path;

    /// <summary>
    /// Gets the dot-separated path the include chain ends at, or <see langword="null"/> when the root include
    /// was not a plain navigation (a filtered include), which cannot be continued.
    /// </summary>
    internal string? Path { get; }

    Specification<T> IIncludableSpecificationBuilder<T, TProperty>.Target => Target;

    string? IIncludableSpecificationBuilder<T, TProperty>.Path => Path;
}

/// <summary>
/// A specification builder positioned on an included navigation of type <typeparamref name="TProperty"/>; the
/// receiver of the <c>ThenInclude</c> extension methods.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <typeparam name="TProperty">
/// The type of the navigation the path currently ends at. Covariant, so a <c>List&lt;OrderLine&gt;</c>
/// navigation is also an <c>IEnumerable&lt;OrderLine&gt;</c> one and <c>ThenInclude</c> binds to the element type.
/// </typeparam>
public interface IIncludableSpecificationBuilder<T, out TProperty> : ISpecification<T>
{
    /// <summary>Gets the specification being built.</summary>
    internal Specification<T> Target { get; }

    /// <summary>Gets the path the chain ends at, or <see langword="null"/> when it cannot be continued.</summary>
    internal string? Path { get; }
}

/// <summary>
/// The <c>ThenInclude</c> extension methods that continue an include path from
/// <see cref="Specification{T}.AddInclude{TProperty}"/> or <see cref="SpecificationBuilder{T}.Include{TProperty}"/>.
/// </summary>
public static class IncludableSpecificationBuilderExtensions
{
    /// <summary>Continues an include path from a collection navigation to a navigation of its elements.</summary>
    /// <typeparam name="T">The entity type the query returns.</typeparam>
    /// <typeparam name="TPrevious">The element type of the collection the path ends at.</typeparam>
    /// <typeparam name="TProperty">The type of the next navigation.</typeparam>
    /// <param name="builder">The builder positioned on the collection navigation.</param>
    /// <param name="navigation">
    /// A plain navigation selector on the element, such as <c>l =&gt; l.Product</c>. Must not be
    /// <see langword="null"/>.
    /// </param>
    /// <returns>A builder positioned on the new navigation.</returns>
    /// <exception cref="NotSupportedException">
    /// The chain started with a filtered include, or <paramref name="navigation"/> is not a plain member path.
    /// </exception>
    public static IncludableSpecificationBuilder<T, TProperty> ThenInclude<T, TPrevious, TProperty>(
        this IIncludableSpecificationBuilder<T, IEnumerable<TPrevious>> builder,
        Expression<Func<TPrevious, TProperty>> navigation) =>
        Continue<T, TProperty>(builder.Target, builder.Path, navigation);

    /// <summary>Continues an include path from a reference navigation to one of its navigations.</summary>
    /// <typeparam name="T">The entity type the query returns.</typeparam>
    /// <typeparam name="TPrevious">The type of the navigation the path ends at.</typeparam>
    /// <typeparam name="TProperty">The type of the next navigation.</typeparam>
    /// <param name="builder">The builder positioned on the reference navigation.</param>
    /// <param name="navigation">
    /// A plain navigation selector, such as <c>c =&gt; c.Address</c>. Must not be <see langword="null"/>.
    /// </param>
    /// <returns>A builder positioned on the new navigation.</returns>
    /// <exception cref="NotSupportedException">
    /// The chain started with a filtered include, or <paramref name="navigation"/> is not a plain member path.
    /// </exception>
    public static IncludableSpecificationBuilder<T, TProperty> ThenInclude<T, TPrevious, TProperty>(
        this IIncludableSpecificationBuilder<T, TPrevious> builder,
        Expression<Func<TPrevious, TProperty>> navigation) =>
        Continue<T, TProperty>(builder.Target, builder.Path, navigation);

    private static IncludableSpecificationBuilder<T, TProperty> Continue<T, TProperty>(
        Specification<T> target,
        string? path,
        LambdaExpression navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        if (path is null)
        {
            throw new NotSupportedException(
                "ThenInclude can only continue a plain navigation path such as o => o.Lines. A filtered include "
                + "(o => o.Lines.Where(...)) cannot be continued; include the deeper navigation separately.");
        }

        var next = SpecificationExpressions.TryGetMemberPath(navigation)
            ?? throw new NotSupportedException(
                $"ThenInclude expects a plain navigation selector such as x => x.Product, but got '{navigation}'.");

        var fullPath = path + "." + next;
        target.AddStringIncludeCore(fullPath);
        return new IncludableSpecificationBuilder<T, TProperty>(target, fullPath);
    }
}

/// <summary>
/// The entry point for building a query specification inline.
/// </summary>
/// <example>
/// <code>
/// var recent = await orders.ListAsync(
///     Spec.For&lt;Order&gt;().Where(o =&gt; o.CustomerId == id).OrderByDescending(o =&gt; o.CreatedOn).Take(10), ct);
/// </code>
/// </example>
public static class Spec
{
    /// <summary>Starts an empty specification over <typeparamref name="T"/>: no criteria, matching every entity.</summary>
    /// <typeparam name="T">The entity type the query returns.</typeparam>
    /// <returns>A new builder.</returns>
    public static SpecificationBuilder<T> For<T>() => new InlineSpecificationBuilder<T>();
}

/// <summary>The builder <see cref="Spec.For{T}"/> returns, writing to a fresh inline specification.</summary>
internal sealed class InlineSpecificationBuilder<T> : SpecificationBuilder<T>
{
    internal InlineSpecificationBuilder()
        : base(new InlineSpecification<T>())
    {
    }
}

/// <summary>The specification behind an inline builder.</summary>
internal sealed class InlineSpecification<T> : Specification<T>;
