using System.Collections.Concurrent;
using SharedKernel.Application.Behaviors.Idempotency;

namespace SharedKernel.Testing.Application;

/// <summary>
/// In-memory fake implementation of <see cref="IIdempotencyKeyStore"/> (<c>05.Application.Behaviors</c>)
/// ONLY, for use in unit tests.
/// </summary>
/// <remarks>
/// Deliberately does <b>not</b> implement <see cref="IIdempotencyResponseStore"/> — use this fake to
/// exercise <c>IdempotentCommandBehavior</c>'s non-replay default path (duplicate submission returns
/// a fresh <c>Result.Failure(Error.Conflict(...))</c>). For response-replay tests, use
/// <see cref="FakeIdempotencyResponseStore"/> instead — a separate concrete type, since C# cannot
/// toggle interface implementation at runtime and <c>IdempotentCommandBehavior</c>'s replay-capability
/// detection depends on the CLR type actually implementing the second interface.
/// </remarks>
/// <remarks>
/// Local-seam-only scope: this type fakes <c>05.Application.Behaviors</c>' own
/// <see cref="IIdempotencyKeyStore"/> exclusively and never references <c>06.Persistence</c>,
/// <c>12.Security</c>, or <c>07.Messaging</c> — in particular it is unrelated to
/// <c>07.Messaging.Abstractions.IIdempotencyStore</c> (consumer-side message deduplication); the two
/// interfaces share a naming pattern but not an owning domain, and this fake never bridges to the
/// messaging one.
/// </remarks>
public sealed class FakeIdempotencyKeyStore : IIdempotencyKeyStore
{
    private readonly ConcurrentDictionary<string, byte> _processedKeys = new();

    /// <summary>Gets every idempotency key recorded as processed so far.</summary>
    public IReadOnlyCollection<string> ProcessedKeys => _processedKeys.Keys.ToArray();

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
}
