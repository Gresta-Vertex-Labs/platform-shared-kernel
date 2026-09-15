using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// An aggregate that reads time from an <see cref="IClock"/>, and that infrastructure can hand a
/// clock to after creating it without calling a domain constructor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> An ORM materializes an aggregate through its parameterless constructor,
/// which cannot receive an <see cref="IClock"/>. The persistence layer therefore attaches the clock
/// right after materialization. Until one is attached, anything that needs the current time, such as
/// raising a timestamped event or soft-deleting, throws <see cref="InvalidOperationException"/>
/// rather than silently recording a meaningless timestamp.
/// </para>
/// <para>
/// This contract is for infrastructure. Domain and application code construct aggregates with a
/// clock and never call <see cref="AttachClock"/>.
/// </para>
/// </remarks>
public interface IHasClock
{
    /// <summary>Gets a value indicating whether a clock has been supplied to this aggregate.</summary>
    bool IsClockAttached { get; }

    /// <summary>
    /// Supplies the clock this aggregate reads time from, replacing any clock already attached.
    /// </summary>
    /// <param name="clock">The clock to use.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    void AttachClock(IClock clock);
}
