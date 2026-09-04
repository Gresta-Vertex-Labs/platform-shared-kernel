using SharedKernel.Cryptography.Totp;

namespace SharedKernel.Security.Totp.Tests.Challenge;

/// <summary>
/// A trivial in-memory <see cref="ITotpReplayGuard"/> — 01.Core ships no default implementation
/// (it inherently requires a backing store), so tests compose their own minimal one, mirroring
/// SharedKernel.Cryptography.Tests's own precedent for this interface.
/// </summary>
internal sealed class FakeTotpReplayGuard : ITotpReplayGuard
{
    private readonly HashSet<(string IdentityKey, string Code)> _used = [];

    public ValueTask<bool> HasBeenUsedAsync(string identityKey, string code, CancellationToken ct = default) =>
        ValueTask.FromResult(_used.Contains((identityKey, code)));

    public ValueTask MarkUsedAsync(string identityKey, string code, TimeSpan validityWindow, CancellationToken ct = default)
    {
        _used.Add((identityKey, code));
        return ValueTask.CompletedTask;
    }
}
