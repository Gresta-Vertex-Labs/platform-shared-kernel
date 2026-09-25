using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Communication.Rest.Handlers;

/// <summary>
/// Header name for opt-in outbound idempotency-key propagation. Like
/// <see cref="CorrelationIdDelegatingHandler.HeaderName"/> / <see cref="TenantIdDelegatingHandler.HeaderName"/>,
/// it is sourced from <c>01.Core</c>'s <see cref="WellKnownHeaders"/>: the receiving service reads the
/// key at its <c>14.Presentation</c> boundary under the same name, and a key sent under any other name
/// is never read. It was once a domain-local <c>"x-idempotency-key"</c>, which that boundary never saw
/// (P-562).
/// </summary>
internal static class IdempotencyHeaders
{
    /// <summary>
    /// The header carrying a stable idempotency key for one logical outbound call
    /// (<c>"Idempotency-Key"</c>).
    /// </summary>
    internal const string IdempotencyKey = WellKnownHeaders.IdempotencyKey;
}

/// <summary>
/// Opt-in handler that injects a stable <c>Idempotency-Key</c> header into outgoing HTTP requests,
/// enabled per typed client via <see cref="Options.RestClientOptions.EnableIdempotencyKeyPropagation"/>.
/// Generates a new hyphenated GUID once per logical call and never regenerates it: because
/// <c>StandardResilienceHandler</c> retries re-send the same <see cref="HttpRequestMessage"/> instance
/// rather than constructing a new one, checking whether the header is already present before generating
/// a value is sufficient to keep the key stable across every retry attempt.
/// Never overwrites a caller-supplied <c>Idempotency-Key</c> header.
/// Registered as transient — holds no cross-request state.
/// </summary>
internal sealed class IdempotencyKeyDelegatingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(IdempotencyHeaders.IdempotencyKey))
        {
            var idempotencyKey = Guid.NewGuid().ToString();
            request.Headers.TryAddWithoutValidation(IdempotencyHeaders.IdempotencyKey, idempotencyKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
