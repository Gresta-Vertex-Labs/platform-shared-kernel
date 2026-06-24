using SharedKernel.Domain.Aggregates;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// Generates a configurable-count list of <typeparamref name="TAggregate"/> instances via an
/// underlying <see cref="AggregateRootFaker{TAggregate, TId}"/>, for seeding bulk
/// <c>AddRangeAsync</c> integration tests.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type to generate.</typeparam>
/// <typeparam name="TId">The aggregate's identity key type.</typeparam>
public sealed class BulkAggregateFaker<TAggregate, TId>
    where TAggregate : AggregateRoot<TId>
    where TId : notnull
{
    private readonly AggregateRootFaker<TAggregate, TId> _faker;

    /// <summary>
    /// Initialises a new <see cref="BulkAggregateFaker{TAggregate, TId}"/> wrapping the supplied
    /// concrete <see cref="AggregateRootFaker{TAggregate, TId}"/> subclass.
    /// </summary>
    /// <param name="faker">The concrete faker that declares the aggregate's generation rules.</param>
    public BulkAggregateFaker(AggregateRootFaker<TAggregate, TId> faker)
    {
        ArgumentNullException.ThrowIfNull(faker);
        _faker = faker;
    }

    /// <summary>Generates <paramref name="count"/> aggregate instances.</summary>
    /// <param name="count">The number of instances to generate. Must be non-negative.</param>
    /// <returns>A list of <paramref name="count"/> generated aggregates.</returns>
    public List<TAggregate> Generate(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return _faker.Generate(count);
    }
}
