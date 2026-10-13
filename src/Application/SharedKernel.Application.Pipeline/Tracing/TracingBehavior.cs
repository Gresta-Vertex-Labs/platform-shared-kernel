using System.Diagnostics;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Shared;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Application.Pipeline.Tracing;

/// <summary>
/// Starts a distributed-tracing <see cref="Activity"/> spanning the inner pipeline for every
/// request.
/// </summary>
/// <typeparam name="TRequest">The request type being traced.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// The outermost behavior in the canonical pipeline — every other behavior's log lines and metric
/// records therefore carry this span's trace id ambiently. The span name is the constant
/// <c>application send</c> (low cardinality); <c>application.request_type</c> (the full type name) and
/// <c>application.request_kind</c> (<c>"command"</c>/<c>"query"</c>/<c>"request"</c>) are recorded as tags instead.
/// </para>
/// <para>
/// Failures are recorded only through the shared recorder (<c>RecordFailure</c>/<c>RecordException</c>): on a
/// <c>Result</c>/<c>Result&lt;T&gt;</c> failure the status is <see cref="ActivityStatusCode.Error"/> with the error code
/// as the description and <c>error.type</c>/<c>error.code</c> are tagged — never thrown; on a thrown exception the
/// description is the exception full name (never its message), the exception event is attached and the exception is
/// rethrown unchanged. The caller's cancellation is not an error: the span status stays unset and the outcome is
/// <c>canceled</c>. The <c>outcome</c> tag is always one of <c>WellKnownOutcomes</c>.
/// </para>
/// <para>
/// <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> returns <see langword="null"/>
/// when no listener is registered, and a <see langword="null"/> <see cref="Activity"/> makes every
/// <c>activity?.</c> call and the <see langword="using"/> disposal a safe no-op — zero allocation
/// cost when tracing is not being collected.
/// </para>
/// </remarks>
internal sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            ApplicationSpanNames.Send,
            ActivityKind.Internal);

        activity?.SetTag(ApplicationTagKeys.RequestType, typeof(TRequest).FullName ?? typeof(TRequest).Name);
        activity?.SetTag(ApplicationTagKeys.RequestKind, RequestKind.Classify<TRequest>());

        try
        {
            var response = await next().ConfigureAwait(false);

            var error = ResponseOutcome.TryGetError(response);
            activity?.SetTag(
                WellKnownTagKeys.Outcome,
                error is null ? WellKnownOutcomes.Success : activity.RecordFailure(error));

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetTag(WellKnownTagKeys.Outcome, activity.RecordException(ex));
            throw;
        }
    }
}
