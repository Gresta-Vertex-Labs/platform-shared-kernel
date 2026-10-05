using System.Globalization;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Communication.Rest.Internal;

/// <summary>
/// When the client has <see cref="RestClientOptions.PropagateIdempotencyKey"/> on, gives every POST and PATCH an
/// <see cref="WellKnownHeaders.IdempotencyKey"/> (<c>Idempotency-Key</c>) header: a new GUID per call, set before the
/// first attempt, so every retry repeats it and the service can tell a retry from a new request.
/// </summary>
/// <remarks>
/// A key the caller set is kept — set one yourself when the call must be recognised across your own re-runs (a
/// redelivered message, a restarted job), derived from what identifies the operation. GET, PUT and DELETE are
/// idempotent by definition and get no key. The header is the one <c>14.Presentation</c>'s
/// <c>[RequireIdempotencyKey]</c> reads.
/// </remarks>
internal sealed class IdempotencyKeyHandler(Func<bool> enabled) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (RestResilience.IsNonIdempotent(request.Method)
            && !request.Headers.Contains(WellKnownHeaders.IdempotencyKey)
            && enabled())
        {
            request.Headers.TryAddWithoutValidation(
                WellKnownHeaders.IdempotencyKey,
                Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
