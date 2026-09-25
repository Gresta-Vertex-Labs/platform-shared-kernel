using SharedKernel.Application.Pipeline.Idempotency;

namespace SharedKernel.Application.Pipeline.Tests.Support;

/// <summary>An in-memory <see cref="IRequestIdempotencyStore"/> double with real reservation semantics.</summary>
internal sealed class FakeIdempotencyStore(List<string>? sequence = null) : IRequestIdempotencyStore
{
    private sealed record Entry(string Fingerprint, bool Completed, string? StoredResponse, string Token);

    private readonly Dictionary<string, Entry> _entries = [];
    private int _tokenCounter;

    public int BeginCallCount { get; private set; }
    public int CompleteCallCount { get; private set; }
    public int ReleaseCallCount { get; private set; }

    public Task<IdempotencyBeginResult> TryBeginAsync(string key, string requestFingerprint, CancellationToken cancellationToken)
    {
        BeginCallCount++;

        if (_entries.TryGetValue(key, out var existing))
        {
            if (existing.Fingerprint != requestFingerprint)
                return Task.FromResult(IdempotencyBeginResult.FingerprintMismatch());

            if (existing.Completed)
                return Task.FromResult(IdempotencyBeginResult.Completed(existing.StoredResponse!));

            return Task.FromResult(IdempotencyBeginResult.InProgress());
        }

        var token = $"token-{++_tokenCounter}";
        _entries[key] = new Entry(requestFingerprint, false, null, token);
        return Task.FromResult(IdempotencyBeginResult.Started(token));
    }

    public Task<bool> CompleteAsync(string key, string reservationToken, string serializedResponse, CancellationToken cancellationToken)
    {
        CompleteCallCount++;

        if (!_entries.TryGetValue(key, out var existing) || existing.Token != reservationToken || existing.Completed)
            return Task.FromResult(false);

        sequence?.Add("idempotency.complete");
        _entries[key] = existing with { Completed = true, StoredResponse = serializedResponse };
        return Task.FromResult(true);
    }

    public Task<bool> ReleaseAsync(string key, string reservationToken, CancellationToken cancellationToken)
    {
        ReleaseCallCount++;

        if (!_entries.TryGetValue(key, out var existing) || existing.Token != reservationToken || existing.Completed)
            return Task.FromResult(false);

        sequence?.Add("idempotency.release");
        _entries.Remove(key);
        return Task.FromResult(true);
    }
}
