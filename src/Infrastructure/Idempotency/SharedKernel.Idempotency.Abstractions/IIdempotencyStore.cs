namespace SharedKernel.Idempotency.Abstractions;

/// <summary>
/// Atomically reserves, completes and releases idempotency keys — the one contract behind both the application
/// pipeline's command idempotency and message-consumer deduplication.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registration.</b> Stores are registered keyed by <see cref="IdempotencyPurpose"/> — through a provider's
/// registration method (<c>AddRedisIdempotency(p =&gt; p.ForRequests().ForMessages())</c>,
/// <c>AddEfCoreIdempotency(...)</c>) or <see cref="IdempotencyServiceCollectionExtensions.AddIdempotencyStore{TStore}(Microsoft.Extensions.DependencyInjection.IServiceCollection, IdempotencyPurpose, Microsoft.Extensions.DependencyInjection.ServiceLifetime)"/>
/// — and resolved with <c>[FromKeyedServices(IdempotencyPurpose.Request)]</c> or
/// <see cref="IdempotencyServiceCollectionExtensions.GetRequiredIdempotencyStore"/>. A service may back each
/// purpose with a different store. The purpose is also passed on every call, so a store serving both
/// keeps the two key spaces apart: a request key can never collide with a message id.
/// </para>
/// <para>
/// <b>Tenant.</b> A store scopes every key by the tenant of the ambient request context
/// (<see cref="SharedKernel.Execution.Context.IRequestContextAccessor"/>), encoded by
/// <see cref="IdempotencyTenantScope"/> — keys of different tenants never collide, and a call with no tenant uses
/// the one shared <see cref="IdempotencyTenantScope.NoTenant"/> scope.
/// </para>
/// <para>
/// <b>Atomicity.</b> <see cref="TryBeginAsync"/> is one compare-and-set against the backing store (a Lua script,
/// an <c>INSERT … ON CONFLICT</c>). A read followed by a write is not a valid implementation: two concurrent
/// callers would both see "free" and both run the guarded work.
/// </para>
/// <para>
/// <b>Semantics.</b> A free or expired key is reserved (<see cref="IdempotencyReservationStatus.Started"/>). An
/// existing key reserved for a different fingerprint reports <see cref="IdempotencyReservationStatus.FingerprintMismatch"/>,
/// whether it is in flight or completed. Otherwise an in-flight key reports
/// <see cref="IdempotencyReservationStatus.InProgress"/> and a completed one
/// <see cref="IdempotencyReservationStatus.Completed"/> with its stored response. A reservation that is never
/// completed or released expires after its <c>ttl</c>, so a crashed caller cannot wedge a key; a completed one
/// expires after its retention window, after which the key is free again.
/// </para>
/// <para>
/// <b>Ownership.</b> <see cref="CompleteAsync"/> and <see cref="ReleaseAsync"/> act only while the supplied token
/// still owns an in-flight reservation. A caller whose reservation expired and was taken over gets
/// <see langword="false"/>, and the new owner's reservation is untouched. A release never removes a completed
/// reservation, so a failed attempt stays retryable and a finished one stays deduplicated.
/// </para>
/// <para>
/// <b>Store outage.</b> An unreachable store throws (fail closed) unless the provider is configured to let the
/// guarded work run anyway (each provider's single <c>AllowExecutionOnStoreUnavailable</c> flag).
/// </para>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>Attempts to reserve <paramref name="key"/>, or reports its existing state.</summary>
    /// <param name="purpose">What the key guards; keeps request keys and message ids apart.</param>
    /// <param name="key">The idempotency key (a request's key, or a message id).</param>
    /// <param name="fingerprint">
    /// Identifies what the key was reserved for. A request passes a fingerprint of its body, so the same key sent
    /// with a different body is rejected; a message passes a fixed value, since its id already identifies it.
    /// </param>
    /// <param name="ttl">
    /// How long a new reservation holds before it expires if never completed or released. Must outlast the guarded
    /// work: once it expires, another caller can reserve the same key.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The reservation outcome.</returns>
    Task<IdempotencyReservation> TryBeginAsync(
        IdempotencyPurpose purpose,
        string key,
        string fingerprint,
        TimeSpan ttl,
        CancellationToken cancellationToken);

    /// <summary>Marks a reservation completed, storing <paramref name="response"/> for replay.</summary>
    /// <param name="purpose">The purpose passed to <see cref="TryBeginAsync"/>.</param>
    /// <param name="key">The reserved key.</param>
    /// <param name="token">The <see cref="IdempotencyReservation.Token"/> of the started reservation.</param>
    /// <param name="response">The serialized response to replay, or <see langword="null"/> to store none.</param>
    /// <param name="retention">How long the completed reservation deduplicates the key.</param>
    /// <param name="cancellationToken">
    /// A token to observe for cancellation. Callers usually pass <see cref="CancellationToken.None"/>: the guarded
    /// work already succeeded, and a cancelled completion would let it run again.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the token still owned an in-flight reservation; <see langword="false"/> if the
    /// reservation was lost (expired and taken over, already completed or released, or a foreign token).
    /// </returns>
    Task<bool> CompleteAsync(
        IdempotencyPurpose purpose,
        string key,
        string token,
        string? response,
        TimeSpan retention,
        CancellationToken cancellationToken);

    /// <summary>Releases a reservation without completing it, so the key can be reserved again at once.</summary>
    /// <param name="purpose">The purpose passed to <see cref="TryBeginAsync"/>.</param>
    /// <param name="key">The reserved key.</param>
    /// <param name="token">The <see cref="IdempotencyReservation.Token"/> of the started reservation.</param>
    /// <param name="cancellationToken">
    /// A token to observe for cancellation. Callers usually pass <see cref="CancellationToken.None"/>: a cancelled
    /// release strands the key until its reservation expires.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the token still owned an in-flight reservation and it was removed;
    /// <see langword="false"/> otherwise. A completed reservation is never released.
    /// </returns>
    Task<bool> ReleaseAsync(
        IdempotencyPurpose purpose,
        string key,
        string token,
        CancellationToken cancellationToken);
}
