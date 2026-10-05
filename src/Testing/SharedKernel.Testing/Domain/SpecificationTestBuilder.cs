using SharedKernel.Domain.Specifications;

namespace SharedKernel.Testing.Domain;

/// <summary>
/// Fluent in-memory test helper for verifying an <see cref="ISpecification{T}"/>'s behavior
/// against a known set of entities, without a database.
/// </summary>
/// <typeparam name="T">The entity type the specification applies to.</typeparam>
/// <remarks>
/// Wraps <see cref="Specification{T}.IsSatisfiedBy"/> — in-domain/in-test use only, per
/// <c>03.Domain</c>'s own documented constraint.
/// </remarks>
public sealed class SpecificationTestBuilder<T>
{
    private readonly ISpecification<T> _spec;
    private IEnumerable<T> _entities = [];
    private int? _expectedCount;
    private Func<T, bool>? _expectedMatch;

    private SpecificationTestBuilder(ISpecification<T> spec) => _spec = spec;

    /// <summary>Begins a new assertion chain for <paramref name="spec"/>.</summary>
    /// <param name="spec">The specification under test.</param>
    /// <returns>A new <see cref="SpecificationTestBuilder{T}"/>.</returns>
    public static SpecificationTestBuilder<T> For(ISpecification<T> spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return new SpecificationTestBuilder<T>(spec);
    }

    /// <summary>Sets the candidate entities to evaluate the specification against.</summary>
    /// <param name="entities">The candidate entities.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public SpecificationTestBuilder<T> Against(IEnumerable<T> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        _entities = entities;
        return this;
    }

    /// <summary>Sets the expected number of matching entities.</summary>
    /// <param name="n">The expected match count.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public SpecificationTestBuilder<T> ExpectCount(int n)
    {
        _expectedCount = n;
        return this;
    }

    /// <summary>Sets an additional predicate every matching entity must satisfy.</summary>
    /// <param name="predicate">The expected per-entity predicate.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public SpecificationTestBuilder<T> ExpectMatch(Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _expectedMatch = predicate;
        return this;
    }

    /// <summary>
    /// Evaluates <c>spec.Criteria</c> against every candidate entity and asserts the configured
    /// expectations.
    /// </summary>
    /// <exception cref="InvalidOperationException">An expectation was not met.</exception>
    public void Assert()
    {
        var compiled = _spec.Criteria?.Compile();
        var matches = _entities.Where(e => compiled is null || compiled(e)).ToList();

        if (_expectedCount is int expectedCount && matches.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} matching entit{(expectedCount == 1 ? "y" : "ies")} but found {matches.Count}. " +
                $"Matches: [{string.Join(", ", matches.Select(m => m?.ToString() ?? "null"))}].");
        }

        if (_expectedMatch is { } predicate)
        {
            var failing = matches.Where(m => !predicate(m)).ToList();
            if (failing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Expected every matching entity to satisfy the additional predicate, but " +
                    $"{failing.Count} did not: [{string.Join(", ", failing.Select(m => m?.ToString() ?? "null"))}].");
            }
        }
    }
}
