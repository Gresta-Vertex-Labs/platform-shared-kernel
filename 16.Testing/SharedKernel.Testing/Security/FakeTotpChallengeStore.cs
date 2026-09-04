using System.Collections.Concurrent;
using SharedKernel.Security.Totp.Challenge;

namespace SharedKernel.Testing.Security;

/// <summary>
/// In-memory fake implementation of <see cref="ITotpChallengeStore"/> for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Records the last successful challenge timestamp per <c>identityKey</c> string — the same
/// shared-helper-formatted <see cref="Guid"/>-to-string conversion <c>TotpChallengeService</c>/
/// <c>TotpStepUpClaimsTransformation</c> use — letting a consuming test seed a fresh-vs-stale
/// challenge state deterministically for <c>[RequireFreshAuthentication]</c>-style assertions.
/// </para>
/// <para>
/// <b>Correction against the original design draft:</b> takes no constructor-injected
/// <see cref="SharedKernel.Primitives.Clocks.IClock"/> — <see cref="ITotpChallengeStore"/>'s real
/// contract already takes <c>verifiedAt</c> as a CALLER-SUPPLIED parameter on
/// <see cref="RecordSuccessfulChallengeAsync"/> (never resolved internally by the store), so clock
/// composability lives at the CALL SITE (e.g. <c>store.RecordSuccessfulChallengeAsync(key, clock.UtcNow, ct)</c>
/// with the existing <c>Clocks/FakeClock</c>), not inside this type — mirroring the real
/// <c>TotpChallengeService</c>'s own identical call shape.
/// </para>
/// </remarks>
public sealed class FakeTotpChallengeStore : ITotpChallengeStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSuccessfulChallenge = new();

    /// <inheritdoc />
    public Task RecordSuccessfulChallengeAsync(string identityKey, DateTimeOffset verifiedAt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        _lastSuccessfulChallenge[identityKey] = verifiedAt;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DateTimeOffset?> TryGetLastSuccessfulChallengeAsync(string identityKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(identityKey);

        return Task.FromResult(_lastSuccessfulChallenge.TryGetValue(identityKey, out var verifiedAt)
            ? verifiedAt
            : (DateTimeOffset?)null);
    }

    /// <summary>Clears every recorded challenge.</summary>
    public void Reset() => _lastSuccessfulChallenge.Clear();
}
