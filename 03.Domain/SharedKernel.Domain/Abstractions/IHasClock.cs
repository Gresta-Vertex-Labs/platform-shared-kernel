using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// An aggregate that reads time from an <see cref="IClock"/> and can be given that clock after an ORM
/// creates it without one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Persistence.</b> An ORM materializes an aggregate through its parameterless constructor, which
/// receives no <see cref="IClock"/>, so the persistence layer attaches the clock right after
/// materialization. Until then, anything that needs the current time, such as raising a timestamped
/// event or soft-deleting, throws <see cref="InvalidOperationException"/> instead of recording a
/// meaningless timestamp.
/// </para>
/// <para>
/// <b>Usage.</b> This contract is for infrastructure. Domain and application code construct aggregates
/// with a clock and never call <see cref="AttachClock"/>.
/// </para>
/// </remarks>
public interface IHasClock
{
    /// <summary>
    /// Gets whether the aggregate has a clock; <see langword="false"/> only for an ORM-materialized
    /// aggregate that has not yet been given one.
    /// </summary>
    bool IsClockAttached { get; }

    /// <summary>
    /// Supplies the clock the aggregate reads time from, replacing any clock already attached.
    /// </summary>
    /// <param name="clock">The clock to use. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    void AttachClock(IClock clock);
}
