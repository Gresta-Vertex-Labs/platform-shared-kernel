namespace SharedKernel.Application.Behaviors.Idempotency;

/// <summary>
/// Optional, additive capability a <see cref="IIdempotencyKeyStore"/> implementation may also
/// implement to opt in to idempotency response replay.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purely additive (WO-039, P-242):</b> a store implementing only <see cref="IIdempotencyKeyStore"/>
/// continues to compile and behave exactly as before this interface existed — duplicate submissions
/// still short-circuit with a fresh <c>Result.Failure(Error.Conflict(...))</c>. A store that
/// <em>also</em> implements this interface additionally persists and replays the original response
/// for a key, so a genuine retry-after-ambiguous-network-outcome returns the ORIGINAL result instead
/// of always <c>Error.Conflict</c>. This never replaces the fail-and-consume-key invariant
/// documented on <see cref="IIdempotencyKeyStore.MarkProcessedAsync"/> — <c>HasProcessedAsync</c>/
/// <c>MarkProcessedAsync</c> are always called first, regardless of replay support.
/// </para>
/// <para>
/// <see cref="IdempotentCommandBehavior{TRequest,TResponse}"/> detects this capability via a plain
/// <see langword="is"/> <see cref="IIdempotencyResponseStore"/> runtime pattern-match on the injected
/// <see cref="IIdempotencyKeyStore"/> instance — a standard .NET optional-capability-interface check
/// (the same class of check as <see cref="IAsyncDisposable"/>/<see cref="IDisposable"/> detection),
/// never reflection.
/// </para>
/// <para>
/// Serialization uses <see cref="System.Text.Json.JsonSerializer"/> (in-box on <c>net10.0</c>, no new
/// NuGet dependency). This package owns the (de)serialization of the response; the store persists
/// whatever string it is handed and never interprets its contents.
/// </para>
/// </remarks>
public interface IIdempotencyResponseStore
{
    /// <summary>
    /// Attempts to retrieve the previously-stored, serialized response for <paramref name="idempotencyKey"/>.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key supplied by the command instance.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// The serialized response string if one was previously stored for this key; otherwise
    /// <see langword="null"/> (e.g. a key marked processed before this capability was adopted).
    /// </returns>
    Task<string?> TryGetStoredResponseAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the serialized response for <paramref name="idempotencyKey"/> so a future duplicate
    /// submission can replay it.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key supplied by the command instance.</param>
    /// <param name="serializedResponse">
    /// The response, already serialized by <see cref="IdempotentCommandBehavior{TRequest,TResponse}"/>
    /// via <see cref="System.Text.Json.JsonSerializer"/>. The store persists this value opaquely — it
    /// never needs to parse or interpret it.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task StoreResponseAsync(string idempotencyKey, string serializedResponse, CancellationToken cancellationToken);
}
