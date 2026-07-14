using System.Diagnostics;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Communication.Rest.Handlers;

/// <summary>
/// Injects the <c>x-correlation-id</c> header into every outgoing HTTP request.
/// Reads <see cref="Activity.Current"/>.Id when an ambient trace is active;
/// falls back to a new GUID when no trace is present.
/// Never overwrites a caller-supplied <c>x-correlation-id</c> header.
/// Registered as transient — holds no cross-request state.
/// </summary>
internal sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    // Sourced from 01.Core's WellKnownHeaders (P-259/P-260) — never an independently-declared literal.
    internal const string HeaderName = WellKnownHeaders.CorrelationId;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(HeaderName))
        {
            var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
            request.Headers.TryAddWithoutValidation(HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
