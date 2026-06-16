using System.Diagnostics;

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
    internal const string HeaderName = "x-correlation-id";

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
