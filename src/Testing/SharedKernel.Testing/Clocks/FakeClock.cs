using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Testing.Clocks;

/// <summary>
/// In-memory fake implementation of <see cref="IClock"/> for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Defaults to a fixed, non-real <see cref="DateTimeOffset"/> when no initial value is supplied —
/// never <see cref="DateTimeOffset.UtcNow"/> — so any test that omits explicit configuration still
/// runs deterministically across time zones and CI machines.
/// </para>
/// <para>Thread-safe via a private lock guarding the backing field.</para>
/// </remarks>
public sealed class FakeClock : IClock
{
    private static readonly DateTimeOffset DefaultInitial = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private DateTimeOffset _utcNow;

    /// <summary>
    /// Initialises a new <see cref="FakeClock"/>.
    /// </summary>
    /// <param name="initial">
    /// The initial value of <see cref="UtcNow"/>. Defaults to a fixed, non-real instant
    /// (<c>2024-01-01T00:00:00Z</c>) when omitted.
    /// </param>
    public FakeClock(DateTimeOffset? initial = null) => _utcNow = initial ?? DefaultInitial;

    /// <inheritdoc />
    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
                return _utcNow;
        }
        set
        {
            lock (_gate)
                _utcNow = value;
        }
    }

    /// <inheritdoc />
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.DateTime);

    /// <summary>Sets <see cref="UtcNow"/> to <paramref name="value"/>.</summary>
    /// <param name="value">The new current time.</param>
    public void Set(DateTimeOffset value) => UtcNow = value;

    /// <summary>Sets <see cref="UtcNow"/> to <paramref name="value"/>. Alias for <see cref="Set"/>.</summary>
    /// <param name="value">The new current time.</param>
    public void SetUtcNow(DateTimeOffset value) => UtcNow = value;

    /// <summary>Advances <see cref="UtcNow"/> by <paramref name="delta"/>.</summary>
    /// <param name="delta">The amount of time to add to the current value.</param>
    public void Advance(TimeSpan delta)
    {
        lock (_gate)
            _utcNow += delta;
    }
}
