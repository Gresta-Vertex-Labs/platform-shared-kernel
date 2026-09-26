using SharedKernel.Idempotency.Abstractions;

namespace SharedKernel.Application.Pipeline.Tests.Support;

/// <summary>An in-memory <see cref="IIdempotencyStore"/> double with real reservation semantics.</summary>
internal sealed class FakeIdempotencyStore(List<string>? sequence = null) : IIdempotencyStore
{
    private sealed record Entry(string Fingerprint, bool Completed, string? StoredResponse, string Token);

    private readonly Dictionary<(IdempotencyPurpose, string), Entry> _entries = [];
    private int _tokenCounter;

    public int BeginCallCount { get; private set; }
    public int CompleteCallCount { get; private set; }
    public int ReleaseCallCount { get; private set; }
    /// <summary>Every key handed to <see cref="TryBeginAsync"/>, in call order.</summary>
    public List<string> BegunKeys { get; } = [];
    public List<IdempotencyPurpose> Purposes { get; } = [];
    public TimeSpan? LastTtl { get; private set; }
    public TimeSpan? LastRetention { get; private set; }

    public Task<IdempotencyReservation> TryBeginAsync(
        IdempotencyPurpose purpose, string key, string fingerprint, TimeSpan ttl, CancellationToken cancellationToken)
    {
        BeginCallCount++;
        BegunKeys.Add(key);
        Purposes.Add(purpose);
        LastTtl = ttl;

        if (_entries.TryGetValue((purpose, key), out var existing))
        {
            if (existing.Fingerprint != fingerprint)
                return Task.FromResult(IdempotencyReservation.FingerprintMismatch());

            if (existing.Completed)
                return Task.FromResult(IdempotencyReservation.Completed(existing.StoredResponse));

            return Task.FromResult(IdempotencyReservation.InProgress());
        }

        var token = $"token-{++_tokenCounter}";
        _entries[(purpose, key)] = new Entry(fingerprint, false, null, token);
        return Task.FromResult(IdempotencyReservation.Started(token));
    }

    public Task<bool> CompleteAsync(
        IdempotencyPurpose purpose, string key, string token, string? response, TimeSpan retention, CancellationToken cancellationToken)
    {
        CompleteCallCount++;
        Purposes.Add(purpose);
        LastRetention = retention;

        if (!_entries.TryGetValue((purpose, key), out var existing) || existing.Token != token || existing.Completed)
            return Task.FromResult(false);

        sequence?.Add("idempotency.complete");
        _entries[(purpose, key)] = existing with { Completed = true, StoredResponse = response };
        return Task.FromResult(true);
    }

    public Task<bool> ReleaseAsync(IdempotencyPurpose purpose, string key, string token, CancellationToken cancellationToken)
    {
        ReleaseCallCount++;
        Purposes.Add(purpose);

        if (!_entries.TryGetValue((purpose, key), out var existing) || existing.Token != token || existing.Completed)
            return Task.FromResult(false);

        sequence?.Add("idempotency.release");
        _entries.Remove((purpose, key));
        return Task.FromResult(true);
    }
}
