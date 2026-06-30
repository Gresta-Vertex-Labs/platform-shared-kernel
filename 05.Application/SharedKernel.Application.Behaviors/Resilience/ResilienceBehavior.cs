using MediatR;
using Polly;
using Polly.Registry;

namespace SharedKernel.Application.Behaviors.Resilience;

/// <summary>
/// Executes the inner pipeline through an injected Polly v8 resilience pipeline implementing
/// retry-with-backoff.
/// </summary>
/// <typeparam name="TRequest">
/// The request type, constrained to <see cref="IRetryableRequest"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// Resolves a named <c>ResiliencePipeline</c> from the injected
/// <see cref="ResiliencePipelineProvider{TKey}"/> keyed by <c>typeof(TRequest).Name</c>, falling
/// back to the platform default key (<see cref="DefaultPipelineKey"/>) when no request-type-specific
/// pipeline has been registered. Circuit-breaking is out of scope for this phase — retry-with-backoff
/// only; a circuit-breaker stage is a deferred, separately-justified follow-up. Exhausted retries
/// surface as either a thrown exception or a propagated <c>Result.Failure</c> — this behavior does
/// not invent a new failure shape; whatever the wrapped pipeline produces on its last attempt is
/// what callers see. Configurable per-request-type via the <see cref="IRetryableRequest"/> marker,
/// never globally fixed.
/// </remarks>
public sealed class ResilienceBehavior<TRequest, TResponse>(ResiliencePipelineProvider<string> pipelineProvider)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRetryableRequest, IRequest<TResponse>
{
    /// <summary>
    /// The resilience pipeline key used when no request-type-specific pipeline has been registered.
    /// </summary>
    public const string DefaultPipelineKey = "SharedKernel.Application.Default";

    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var key = pipelineProvider.TryGetPipeline<TResponse>(requestName, out _)
            ? requestName
            : DefaultPipelineKey;
        var pipeline = pipelineProvider.GetPipeline<TResponse>(key);

        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        try
        {
            return await pipeline
                .ExecuteAsync(
                    static (ResilienceContext _, RequestHandlerDelegate<TResponse> state) =>
                        new ValueTask<TResponse>(state()),
                    context,
                    next)
                .ConfigureAwait(false);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}
