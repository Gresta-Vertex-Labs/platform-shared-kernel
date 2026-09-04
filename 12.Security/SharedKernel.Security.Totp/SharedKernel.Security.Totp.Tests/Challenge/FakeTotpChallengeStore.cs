using SharedKernel.Security.Totp.Challenge;

namespace SharedKernel.Security.Totp.Tests.Challenge;

/// <summary>
/// A call-counting, in-memory <see cref="ITotpChallengeStore"/> test double — proves both "is this
/// value stored correctly" and "was this store invoked at all" (T-42).
/// </summary>
internal sealed class FakeTotpChallengeStore : ITotpChallengeStore
{
    private readonly Dictionary<string, DateTimeOffset> _lastChallenge = [];

    public int RecordCallCount { get; private set; }

    public int TryGetCallCount { get; private set; }

    public Task RecordSuccessfulChallengeAsync(string identityKey, DateTimeOffset verifiedAt, CancellationToken ct = default)
    {
        RecordCallCount++;
        _lastChallenge[identityKey] = verifiedAt;
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> TryGetLastSuccessfulChallengeAsync(string identityKey, CancellationToken ct = default)
    {
        TryGetCallCount++;
        return Task.FromResult(_lastChallenge.TryGetValue(identityKey, out var value) ? value : (DateTimeOffset?)null);
    }
}
