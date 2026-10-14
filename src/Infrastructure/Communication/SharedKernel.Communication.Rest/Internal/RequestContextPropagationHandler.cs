using Microsoft.Extensions.Logging;
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
/// same values. A failure is logged (11200) with its exception; the request is still sent.
/// </para>
/// </remarks>
internal sealed class RequestContextPropagationHandler(
    IRequestContextAccessor accessor,
    ILogger<RequestContextPropagationHandler> logger) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Once per call, outside the retries: the Result verbs reach this client's logger through the request.
        RestLogger.Attach(request, logger);

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
        catch (Exception exception)
        {
            // Best effort: a header that cannot be written must not fail the call, but it is never silent.
            CommunicationRestLog.PropagationFailed(logger, exception);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
