using System.Collections.Concurrent;
using SharedKernel.Security.Oidc.Dpop;

namespace SharedKernel.Testing.Security;

/// <summary>An in-memory <see cref="IDpopReplayCache"/> for tests. Entries never expire.</summary>
/// <remarks>
/// <c>AddDpop</c> registers the cache as scoped, which would give every request an empty instance. Register one
/// instance first so replays across requests are detected:
/// <code>services.AddSingleton&lt;IDpopReplayCache&gt;(new InMemoryDpopReplayCache());</code>
/// </remarks>
public sealed class InMemoryDpopReplayCache : IDpopReplayCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _proofs = new(StringComparer.Ordinal);

    /// <summary>Gets the number of recorded proofs.</summary>
    public int Count => _proofs.Count;

    /// <inheritdoc/>
    public ValueTask<bool> TryAddAsync(string proofId, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proofId);
        return ValueTask.FromResult(_proofs.TryAdd(proofId, expiresAt));
    }
}
