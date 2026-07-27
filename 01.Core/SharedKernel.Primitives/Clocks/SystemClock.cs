namespace SharedKernel.Primitives.Clocks;

/// <summary>
/// Production implementation of <see cref="IClock"/> that sources the current time from a
/// <see cref="System.TimeProvider"/>. Register as a singleton.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TimeProvider"/> (shipped in the BCL since .NET 8) is an internal implementation
/// detail of this class only — it is never an alternative time source exposed to
/// domain/application call sites. <see cref="IClock"/> remains the sole time abstraction those
/// call sites depend on; its own public surface (<see cref="UtcNow"/>, <see cref="Today"/>) is
/// completely unchanged by this class reading from a <see cref="TimeProvider"/> underneath.
/// Injecting <see cref="TimeProvider"/> directly anywhere outside this class is not a sanctioned
/// pattern on this platform, and <c>SK0001</c> (the analyzer flagging direct
/// <see cref="DateTime.UtcNow"/>/<see cref="DateTimeOffset.UtcNow"/> usage) is unaffected by this
/// internal wiring change.
/// </para>
/// <para>
/// This class holds no mutable state beyond the held <see cref="TimeProvider"/> reference — every
/// property access reads directly from it.
/// </para>
/// </remarks>
public sealed class SystemClock : IClock
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance backed by <see cref="TimeProvider.System"/> — the real system
    /// clock. This is the constructor production hosts should use via <c>services.AddClock()</c>.
    /// </summary>
    public SystemClock()
        : this(TimeProvider.System) { }

    /// <summary>
    /// Initializes a new instance backed by the supplied <paramref name="timeProvider"/>.
    /// </summary>
    /// <param name="timeProvider">
    /// The time source <see cref="UtcNow"/> and <see cref="Today"/> read from. Use this overload
    /// when a host already has its own shared, custom <see cref="TimeProvider"/> registered (e.g.
    /// for coordinated simulation or deterministic replay) and wants <see cref="SystemClock"/> to
    /// reuse that same instance instead of <see cref="TimeProvider.System"/>. Tests can also pass a
    /// fake/fixed <see cref="TimeProvider"/> directly.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public SystemClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <inheritdoc/>
    public DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
}
