using SharedKernel.Execution.Context;

namespace SharedKernel.Communication.Rest.Handlers;

/// <summary>
/// Writes the current caller onto every outgoing HTTP request: the correlation id, the tenant, the actor and the
/// client, under <c>01.Core</c>'s <c>WellKnownHeaders</c> names (<see cref="RequestContextPropagation"/>).
/// </summary>
/// <remarks>
/// <para>
/// The caller is <see cref="IRequestContextAccessor.Current"/>, the context every inbound adapter makes ambient —
/// the HTTP request-context middleware, the gRPC server interceptor, the message consume filter, the workflow
/// activity interceptor and the scheduler. No <c>IHttpContextAccessor</c> is involved, so a call made from a
/// message consumer or a background job carries its caller exactly like one made from an HTTP request.
/// </para>
/// <para>
/// <b>The correlation id is the caller's, never <c>Activity.Id</c></b> (which changes whenever a new trace starts):
/// <see cref="CorrelationIds.Current"/>, else a new id when the call is the first hop of a new operation.
/// </para>
/// <para>
/// Never overwrites a header the caller set on the request, and never throws: a propagation failure must not fail
/// the call. Registered before the resilience handler, so it runs once per logical call and every retry re-sends
/// the same values.
/// </para>
/// </remarks>
/// <param name="accessor">Reads the ambient request context.</param>
internal sealed class RequestContextDelegatingHandler(IRequestContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = accessor.Current;
            RequestContextPropagation.WriteHeaders(
                context,
                request.Headers,
                static (headers, name, value) =>
                {
                    if (!headers.Contains(name))
                        headers.TryAddWithoutValidation(name, value);
                },
                CorrelationIds.Current(context) ?? CorrelationIds.New());
        }
        catch
        {
            // Best-effort propagation: a header-injection failure must never fail the outgoing request.
        }

        return base.SendAsync(request, cancellationToken);
    }
}
