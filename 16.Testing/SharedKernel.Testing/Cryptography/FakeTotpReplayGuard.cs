using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// An in-memory <see cref="ITotpReplayGuard"/> that keeps the last accepted time step per identity until its
/// retention expires on the injected clock.
/// </summary>
public sealed class FakeTotpReplayGuard : ITotpReplayGuard
{
    private readonly IClock _clock;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (long TimeStep, DateTimeOffset ExpiresAt)> _accepted = new(StringComparer.Ordinal);

    /// <summary>Creates the guard.</summary>
    /// <param name="clock">The clock deciding when a record expires; a new <see cref="FakeClock"/> when omitted.</param>
    public FakeTotpReplayGuard(IClock? clock = null) => _clock = clock ?? new FakeClock();

    /// <inheritdoc />
    public ValueTask<bool> TryAcceptTimeStepAsync(
        string identityKey,
        long timeStep,
        TimeSpan retention,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        DateTimeOffset now = _clock.UtcNow;
        lock (_gate)
        {
            if (_accepted.TryGetValue(identityKey, out var last) && last.ExpiresAt > now && last.TimeStep >= timeStep)
            {
                return new(false);
            }

            _accepted[identityKey] = (timeStep, now + retention);
            return new(true);
        }
    }

    /// <summary>Forgets every accepted time step.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _accepted.Clear();
        }
    }
}
