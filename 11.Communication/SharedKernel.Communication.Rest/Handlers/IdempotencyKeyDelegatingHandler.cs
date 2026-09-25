using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Communication.Rest.Handlers;

/// <summary>
/// Opt-in handler that injects a stable <see cref="WellKnownHeaders.IdempotencyKey"/> (<c>Idempotency-Key</c>)
/// header into outgoing HTTP requests, enabled per typed client via
/// <see cref="Options.RestClientOptions.EnableIdempotencyKeyPropagation"/>.
/// Generates a new hyphenated GUID once per logical call and never regenerates it: because
/// <c>StandardResilienceHandler</c> retries re-send the same <see cref="HttpRequestMessage"/> instance
/// rather than constructing a new one, checking whether the header is already present before generating
/// a value is sufficient to keep the key stable across every retry attempt.
/// Never overwrites a caller-supplied key. Registered as transient — holds no cross-request state.
/// </summary>
/// <remarks>
/// The header name is the one <c>14.Presentation</c>'s <c>[RequireIdempotencyKey]</c> reads. It used to be a
/// package-local <c>x-idempotency-key</c>, which no inbound endpoint read, so the key never arrived (P-566).
/// </remarks>
internal sealed class IdempotencyKeyDelegatingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(WellKnownHeaders.IdempotencyKey))
        {
            var idempotencyKey = Guid.NewGuid().ToString();
            request.Headers.TryAddWithoutValidation(WellKnownHeaders.IdempotencyKey, idempotencyKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
