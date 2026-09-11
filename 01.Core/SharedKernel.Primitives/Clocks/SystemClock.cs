namespace SharedKernel.Primitives.Clocks;

/// <summary>
/// The production <see cref="IClock"/>, reading the real time from a
/// <see cref="System.TimeProvider"/>. Register as a singleton, normally via
/// <see cref="ClockExtensions.AddClock"/>.
/// </summary>
/// <remarks>
/// <para>
/// Holds no mutable state beyond the <see cref="TimeProvider"/> reference it was given, so a
/// single instance is safe to share across threads for the lifetime of the process.
/// </para>
/// <para>
/// <b>This is the one type on the platform allowed to depend on <see cref="TimeProvider"/>.</b>
/// Everything else injects <see cref="IClock"/>. Keeping the dependency here means the platform
/// has exactly one place where wall-clock time enters, and <see cref="IClock"/>'s two-member
/// surface stays the only thing a test has to substitute.
/// </para>
/// </remarks>
public sealed class SystemClock : IClock
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a clock reading from <see cref="TimeProvider.System"/> — the real system clock.
    /// </summary>
    public SystemClock()
        : this(TimeProvider.System) { }

    /// <summary>
    /// Creates a clock reading from the supplied <paramref name="timeProvider"/>.
    /// </summary>
    /// <param name="timeProvider">The time source to read from.</param>
    /// <remarks>
    /// <para>
    /// Use this when the host already owns a shared custom <see cref="TimeProvider"/> — for
    /// coordinated simulation, or deterministic replay across several components that must agree on
    /// "now" — and you want this clock reading from that same instance rather than the real one.
    /// Tests can also pass a fixed provider directly, though registering a fake
    /// <see cref="IClock"/> is usually simpler.
    /// </para>
    /// <para>
    /// <b>A host does not normally call this itself.</b> If a <see cref="TimeProvider"/> is
    /// registered in the container, <see cref="ClockExtensions.AddClock"/> already routes through
    /// this constructor — see that method's remarks for why.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public SystemClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <inheritdoc/>
    // Goes through UtcDateTime rather than DateTimeOffset.Date so the result is the UTC date, not
    // the date of the provider's offset -- those differ either side of midnight for a non-zero
    // offset, and IClock.Today is documented as UTC.
    public DateOnly Today => DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
}
