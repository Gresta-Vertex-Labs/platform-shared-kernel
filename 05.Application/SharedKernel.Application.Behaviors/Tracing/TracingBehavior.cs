using MediatR;
using SharedKernel.Application.Behaviors.Metrics;

namespace SharedKernel.Application.Behaviors.Tracing;

/// <summary>
/// Starts a distributed-tracing <see cref="System.Diagnostics.Activity"/> spanning the inner
/// pipeline for every request.
/// </summary>
/// <typeparam name="TRequest">The request type being traced.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// A behavior distinct from <see cref="MetricsBehavior{TRequest,TResponse}"/> — single
/// responsibility (one behavior measures, one behavior traces), even though both occupy the same
/// outermost-but-inside-Logging band. Mirrors <c>07.Messaging</c>'s
/// <c>ConsumerBase.Consume</c>/<c>MassTransitEventPublisher.Publish</c> shape exactly: the activity
/// is started before <c>next()</c> and disposed (via <c>using</c>) after, regardless of success,
/// <c>Result.Failure</c>, or thrown exception. <c>StartActivity</c> returns <see langword="null"/>
/// when no listener is registered, and a <see langword="null"/> <see cref="System.Diagnostics.Activity"/>
/// makes <c>activity?.SetTag</c> and disposal both safe no-ops — zero allocation cost when tracing
/// is not being collected. Participates in the ambient <c>Activity.Current</c> trace context
/// exactly as <c>StartActivity</c> already does by BCL default — no custom propagation logic.
/// The <c>request.name</c> tag uses <c>typeof(TRequest).FullName ?? typeof(TRequest).Name</c> to
/// prevent tag collisions when two assemblies in the same host define a request type with the same
/// short name.
/// </remarks>
public sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity("Request.Handle");
        // Use FullName to prevent tag collisions when two assemblies define a same-named request type.
        activity?.SetTag("request.name", typeof(TRequest).FullName ?? typeof(TRequest).Name);

        return await next().ConfigureAwait(false);
    }
}
