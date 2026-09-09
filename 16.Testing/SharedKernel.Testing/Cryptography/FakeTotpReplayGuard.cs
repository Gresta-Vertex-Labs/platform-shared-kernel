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
/// <b>BREAKING MIGRATION (P-527/WO-083):</b> the prior two-member <c>HasBeenUsedAsync</c>/
/// <c>MarkUsedAsync</c> shape has been REMOVED — not kept as a parallel overload — and replaced by
/// the single atomic <see cref="ITotpReplayGuard.TryMarkUsedAsync"/> member, mirroring
/// <c>01.Core</c>'s own P-514 contract change. The claim logic is a genuine lock-free
/// compare-and-set retry loop over a <see cref="ConcurrentDictionary{TKey,TValue}"/> — never a
/// separate read followed by a separate write. Every state transition is decided by the
/// authoritative boolean return of <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> or
/// <see cref="ConcurrentDictionary{TKey,TValue}.TryUpdate(TKey,TValue,TValue)"/> itself, never
/// inferred from an earlier, possibly-stale read — an initial <c>TryGetValue</c> only selects which
/// atomic primitive to attempt next; on a lost race the loop re-reads current state and retries. A
/// trivially-sequential "check `HasBeenUsedAsync`, then call `MarkUsedAsync`" implementation would
/// let two concurrent claims for the same code both observe "not yet used" and both succeed — the
/// exact TOCTOU this atomic contract exists to close — and would let a downstream concurrency test
/// pass vacuously even against a genuinely broken production implementation, defeating the entire
/// purpose of proving this fake atomic.
/// </para>
/// <para>
/// Records each successful claim's expiry as <c>clock.UtcNow + validityWindow</c>; an entry whose
/// expiry has already elapsed per the injected <see cref="IClock"/> is treated as ABSENT by the
/// atomic claim — a fresh <see cref="TryMarkUsedAsync"/> call for that same
/// <c>(identityKey, code)</c> succeeds and re-claims it, exactly mirroring the real store's
/// documented "entry expiring after <c>validityWindow</c>" behavior. Defaults its injected
/// <see cref="IClock"/> to a fresh <see cref="FakeClock"/> (a fixed, non-real instant), never a
/// real-wall-clock <c>SystemClock</c> — this package's own hard rule forbids real
/// <see cref="DateTimeOffset.UtcNow"/> anywhere outside the deliberate <c>Containers/</c> fixtures,
/// so a "defaults to the real clock" fake would violate that rule for every zero-config test.
/// Composable with a caller-supplied <see cref="Clocks.FakeClock"/> for manual time-window control
/// the same way every other clock-aware fake in this package is.
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
    /// <remarks>
    /// Genuinely atomic: a lock-free compare-and-set retry loop, never a separate check-then-act
    /// two-step. See the type-level remarks for the full rationale.
    /// </remarks>
    public ValueTask<bool> TryMarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identityKey);
        ArgumentNullException.ThrowIfNull(code);

        var key = (identityKey, code);
        var now = _clock.UtcNow;
        var newExpiry = now + validityWindow;

        while (true)
        {
            if (_usedUntil.TryGetValue(key, out var existingExpiry))
            {
                if (existingExpiry > now)
                {
                    // Still within a previously-winning claim's validity window — a replay.
                    return ValueTask.FromResult(false);
                }

                // The recorded entry has expired per the injected clock — treated as absent.
                // Atomically replace it ONLY if it still holds the exact stale value just read;
                // TryUpdate's own boolean return is the authoritative decision. A lost race means
                // another caller changed the entry between our read and this call — loop and
                // re-evaluate against the now-current state, never assume our stale read still holds.
                if (_usedUntil.TryUpdate(key, newExpiry, existingExpiry))
                {
                    return ValueTask.FromResult(true);
                }
            }
            else if (_usedUntil.TryAdd(key, newExpiry))
            {
                // No entry existed — this call is the first to claim a fresh code. TryAdd is itself
                // the atomic primitive: if two callers race here, only one TryAdd succeeds.
                return ValueTask.FromResult(true);
            }

            // Lost the race — TryAdd/TryUpdate failed because another caller concurrently changed
            // the entry between our read and our attempted write. Retry against fresh state.
        }
    }

    /// <summary>Clears every recorded usage.</summary>
    public void Reset() => _usedUntil.Clear();
}
