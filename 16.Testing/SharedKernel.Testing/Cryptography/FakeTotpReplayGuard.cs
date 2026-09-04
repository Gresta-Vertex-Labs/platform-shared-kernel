using System.Collections.Concurrent;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// In-memory fake implementation of <see cref="ITotpReplayGuard"/> for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// <c>01.Core/SharedKernel.Cryptography</c> ships NO default implementation of this interface by
/// deliberate design — a consuming service supplies its own store-backed implementation. This fake
/// is therefore genuinely load-bearing for any test exercising <see cref="TotpVerifier"/>'s replay
/// path, not a redundant stand-in for an implementation that already exists.
/// </para>
/// <para>
/// <b>Correction against the original design draft:</b> defaults its injected <see cref="IClock"/>
/// to a fresh <see cref="FakeClock"/> (a fixed, non-real instant), never a real-wall-clock
/// <c>SystemClock</c> — this package's own hard rule forbids real <see cref="DateTimeOffset.UtcNow"/>
/// anywhere outside the deliberate <c>Containers/</c> fixtures, so a "defaults to the real clock"
/// fake would violate that rule for every zero-config test. Composable with a caller-supplied
/// <see cref="Clocks.FakeClock"/> for manual time-window control the same way every other
/// clock-aware fake in this package is.
/// </para>
/// <para>
/// Records each <see cref="MarkUsedAsync"/> call's expiry as <c>clock.UtcNow + validityWindow</c>;
/// <see cref="HasBeenUsedAsync"/> reports <see langword="true"/> only while that expiry has not yet
/// elapsed per the injected clock — advancing a caller-supplied <see cref="FakeClock"/> past the
/// window makes the SAME code presentable again, exactly mirroring the real store's documented
/// "entry expiring after <c>validityWindow</c>" behavior.
/// </para>
/// </remarks>
public sealed class FakeTotpReplayGuard : ITotpReplayGuard
{
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<(string IdentityKey, string Code), DateTimeOffset> _usedUntil = new();

    /// <summary>Initialises a new <see cref="FakeTotpReplayGuard"/>.</summary>
    /// <param name="clock">
    /// The source of "now" for expiry comparisons. Defaults to a fresh <see cref="FakeClock"/> when
    /// omitted.
    /// </param>
    public FakeTotpReplayGuard(IClock? clock = null) => _clock = clock ?? new FakeClock();

    /// <inheritdoc />
    public ValueTask<bool> HasBeenUsedAsync(string identityKey, string code, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identityKey);
        ArgumentNullException.ThrowIfNull(code);

        var hasBeenUsed = _usedUntil.TryGetValue((identityKey, code), out var expiresAt) && _clock.UtcNow < expiresAt;
        return ValueTask.FromResult(hasBeenUsed);
    }

    /// <inheritdoc />
    public ValueTask MarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identityKey);
        ArgumentNullException.ThrowIfNull(code);

        _usedUntil[(identityKey, code)] = _clock.UtcNow + validityWindow;
        return ValueTask.CompletedTask;
    }

    /// <summary>Clears every recorded usage.</summary>
    public void Reset() => _usedUntil.Clear();
}
