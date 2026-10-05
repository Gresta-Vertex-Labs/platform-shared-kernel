using SharedKernel.Execution.Context;

namespace SharedKernel.Communication.Rest.Internal;

/// <summary>
/// Writes the current caller onto every outgoing request: the correlation id, the tenant, the actor and the client,
/// under <c>01.Core</c>'s <c>WellKnownHeaders</c> names (<see cref="RequestContextPropagation"/>).
/// </summary>
/// <remarks>
/// <para>
/// The caller is <see cref="IRequestContextAccessor.Current"/>, the context every inbound adapter makes ambient — the
/// HTTP request-context middleware, the gRPC server interceptor, the message consume filter, the workflow activity
/// interceptor and the scheduler — so a call made from a consumer or a job carries its caller like one made from an
/// HTTP request.
/// </para>
/// <para>
/// The correlation id is the caller's (<see cref="CorrelationIds.Current"/>), never <c>Activity.Id</c>; a call that
/// starts a new operation gets a new one. A header the request already has is kept, and a propagation failure never
/// fails the call. Registered outside the resilience handler, so it runs once per call and every retry re-sends the
/// same values.
/// </para>
/// </remarks>
internal sealed class RequestContextPropagationHandler(IRequestContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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
                    {
                        headers.TryAddWithoutValidation(name, value);
                    }
                },
                CorrelationIds.Current(context) ?? CorrelationIds.New());
        }
        catch (Exception)
        {
            // Best effort: a header that cannot be written must not fail the call.
        }

        return base.SendAsync(request, cancellationToken);
    }
}
