using System.Diagnostics;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Shared;

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
/// records therefore carry this span's trace id ambiently. The span name is
/// <c>typeof(TRequest).Name</c> (the short type name — low cardinality, safe as a span name);
/// <c>request.type</c> (the full type name) and <c>request.kind</c> (<c>"command"</c>/<c>"query"</c>/<c>"request"</c>)
/// are recorded as tags instead.
/// </para>
/// <para>
/// On a <c>Result</c>/<c>Result&lt;T&gt;</c> failure, the span status is set to
/// <see cref="ActivityStatusCode.Error"/> with the error code as the description, and
/// <c>error.type</c>/<c>error.code</c> tags are added — never thrown. On a thrown exception, the
/// span status is likewise set to <see cref="ActivityStatusCode.Error"/>, an exception event is
/// attached via <c>Activity.AddException</c>, and the exception is rethrown unchanged.
/// </para>
/// <para>
/// <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> returns <see langword="null"/>
/// when no listener is registered, and a <see langword="null"/> <see cref="Activity"/> makes every
/// <c>activity?.</c> call and the <see langword="using"/> disposal a safe no-op — zero allocation
/// cost when tracing is not being collected.
/// </para>
/// </remarks>
public sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            typeof(TRequest).Name,
            ActivityKind.Internal);

        activity?.SetTag("request.type", typeof(TRequest).FullName ?? typeof(TRequest).Name);
        activity?.SetTag("request.kind", RequestKind.Classify<TRequest>());

        try
        {
            var response = await next().ConfigureAwait(false);

            var error = ResponseOutcome.TryGetError(response);
            if (error is not null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, error.Code);
                activity?.SetTag("error.type", error.Type.ToString());
                activity?.SetTag("error.code", error.Code);
            }

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().FullName);
            activity?.AddException(ex);
            throw;
        }
    }
}
