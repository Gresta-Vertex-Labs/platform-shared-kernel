namespace SharedKernel.Communication.Rest.Handlers;

/// <summary>
/// Domain-local header name for opt-in outbound idempotency-key propagation. Unlike
/// <see cref="CorrelationIdDelegatingHandler.HeaderName"/> / <see cref="TenantIdDelegatingHandler.HeaderName"/>,
/// this value is not sourced from <c>01.Core</c>'s <c>WellKnownHeaders</c> — idempotency-key propagation
/// is a capability local to this package, not a cross-domain propagation identifier.
/// </summary>
internal static class IdempotencyHeaders
{
    /// <summary>The header carrying a stable idempotency key for one logical outbound call.</summary>
    internal const string IdempotencyKey = "x-idempotency-key";
}

/// <summary>
/// Opt-in handler that injects a stable <c>x-idempotency-key</c> header into outgoing HTTP requests,
/// enabled per typed client via <see cref="Options.RestClientOptions.EnableIdempotencyKeyPropagation"/>.
/// Generates a new hyphenated GUID once per logical call and never regenerates it: because
/// <c>StandardResilienceHandler</c> retries re-send the same <see cref="HttpRequestMessage"/> instance
/// rather than constructing a new one, checking whether the header is already present before generating
/// a value is sufficient to keep the key stable across every retry attempt.
/// Never overwrites a caller-supplied <c>x-idempotency-key</c> header.
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
