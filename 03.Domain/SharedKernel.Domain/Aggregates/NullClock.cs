using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// Sentinel clock used exclusively by the ORM-materialization path (parameterless constructor)
/// of <see cref="AggregateRoot{TId}"/>. Returns <see cref="DateTimeOffset.MinValue"/> for all
/// time queries — this value is never written to domain events in production.
/// </summary>
internal sealed class NullClock : IClock
{
    /// <summary>The singleton instance.</summary>
    internal static readonly NullClock Instance = new();

    private NullClock() { }

    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.MinValue;

    /// <inheritdoc/>
    public DateOnly Today => DateOnly.MinValue;
}
