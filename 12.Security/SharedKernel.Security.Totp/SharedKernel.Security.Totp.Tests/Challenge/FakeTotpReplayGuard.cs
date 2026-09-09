using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Security.Totp.Tests.Challenge;

/// <summary>
/// A trivial in-memory <see cref="ITotpReplayGuard"/> — 01.Core ships no default implementation
/// (it inherently requires a backing store), so tests compose their own minimal one, mirroring
/// SharedKernel.Cryptography.Tests's own precedent for this interface.
/// </summary>
/// <remarks>
/// Migrated onto <see cref="ITotpReplayGuard"/>'s single atomic <see cref="TryMarkUsedAsync"/> member
/// (P-514/WO-083, BREAKING). This fake is local to this package's own test suite and is not the
/// vehicle for a downstream concurrency proof (unlike <c>16.Testing</c>'s shared
/// <c>FakeTotpReplayGuard</c>, P-527) — but it must still correctly implement the single-member
/// contract rather than regress to a check-then-add shape that could silently accept a double-claim.
/// <see cref="HashSet{T}.Add"/>'s own return value (<see langword="false"/> when the element was
/// already present) is what makes the check-and-mark a single indivisible call here.
/// </remarks>
internal sealed class FakeTotpReplayGuard : ITotpReplayGuard
{
    private readonly HashSet<(string IdentityKey, string Code)> _used = [];

    public ValueTask<bool> TryMarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default) =>
        ValueTask.FromResult(_used.Add((identityKey, code)));
}
