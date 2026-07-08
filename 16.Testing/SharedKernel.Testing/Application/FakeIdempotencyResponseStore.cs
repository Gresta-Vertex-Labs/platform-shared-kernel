using System.Collections.Concurrent;
using SharedKernel.Application.Behaviors.Idempotency;

namespace SharedKernel.Testing.Application;

/// <summary>
/// In-memory fake implementation of BOTH <see cref="IIdempotencyKeyStore"/> and
/// <see cref="IIdempotencyResponseStore"/> (<c>05.Application.Behaviors</c>), for use in unit tests
/// that exercise idempotency response replay.
/// </summary>
/// <remarks>
/// Implements both interfaces so <c>IdempotentCommandBehavior</c>'s <c>is IIdempotencyResponseStore</c>
/// runtime pattern-match succeeds — this is the fake to register for response-replay end-to-end
/// tests. This MUST be a separate concrete type from <see cref="FakeIdempotencyKeyStore"/>, not a
/// constructor flag on one type: C# cannot toggle interface implementation at runtime, and
/// <c>IdempotentCommandBehavior</c>'s replay-capability detection depends on the CLR type actually
/// implementing the second interface. A test seeds via <see cref="MarkAsProcessed"/> +
/// <see cref="StoreResponseAsync"/> (or lets a first dispatch populate both naturally) then dispatches
/// a second request with the same key and asserts the ORIGINAL response is replayed rather than a
/// fresh <c>Error.Conflict</c>.
/// </remarks>
/// <remarks>
/// Local-seam-only scope: this type fakes <c>05.Application.Behaviors</c>' own
/// <see cref="IIdempotencyKeyStore"/>/<see cref="IIdempotencyResponseStore"/> pair exclusively and
/// never references <c>06.Persistence</c>, <c>12.Security</c>, or <c>07.Messaging</c> — in particular
/// it is unrelated to <c>07.Messaging.Abstractions.IIdempotencyStore</c> (consumer-side message
/// deduplication); the two interfaces share a naming pattern but not an owning domain.
/// </remarks>
public sealed class FakeIdempotencyResponseStore : IIdempotencyKeyStore, IIdempotencyResponseStore
{
    private readonly ConcurrentDictionary<string, byte> _processedKeys = new();
    private readonly ConcurrentDictionary<string, string> _storedResponses = new();

    /// <summary>Gets every idempotency key recorded as processed so far.</summary>
    public IReadOnlyCollection<string> ProcessedKeys => _processedKeys.Keys.ToArray();

    /// <summary>Gets every stored response, keyed by idempotency key.</summary>
    public IReadOnlyDictionary<string, string> StoredResponses => _storedResponses;

    /// <summary>
    /// Pre-seeds <paramref name="idempotencyKey"/> as already processed without going through
    /// <see cref="MarkProcessedAsync"/> — simulates "this key was already consumed by a prior run".
    /// </summary>
    public void MarkAsProcessed(string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        _processedKeys[idempotencyKey] = 0;
    }

    /// <inheritdoc />
    public Task<bool> HasProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        return Task.FromResult(_processedKeys.ContainsKey(idempotencyKey));
    }

    /// <inheritdoc />
    public Task MarkProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        _processedKeys[idempotencyKey] = 0;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> TryGetStoredResponseAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        return Task.FromResult(_storedResponses.TryGetValue(idempotencyKey, out var response) ? response : null);
    }

    /// <inheritdoc />
    public Task StoreResponseAsync(string idempotencyKey, string serializedResponse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        ArgumentNullException.ThrowIfNull(serializedResponse);
        _storedResponses[idempotencyKey] = serializedResponse;
        return Task.CompletedTask;
    }
}
