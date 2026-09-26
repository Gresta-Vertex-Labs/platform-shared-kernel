using SharedKernel.Application.Messaging;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// Runs one request through its pipeline behaviors and then its handler.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// <para>
/// The mediator-independent core of request dispatch. The behaviors are every
/// <see cref="IPipelineBehavior{TRequest,TResponse}"/> registration whose generic constraints the
/// request satisfies, in registration order, the first one outermost —
/// <c>AddSharedKernelApplication</c> registers them in the fixed stage
/// order, so the order never depends on the calls a service happens to make.
/// </para>
/// <para>
/// Registered open-generic (transient) by <c>AddSharedKernelApplication</c>.
/// A mediator adapter resolves it from the request's DI scope and calls <see cref="HandleAsync"/>;
/// a test can do the same without any mediator.
/// </para>
/// </remarks>
/// <param name="handler">The request's handler.</param>
/// <param name="behaviors">The behaviors applicable to the request, outermost first.</param>
public sealed class RequestPipeline<TRequest, TResponse>(
    IRequestHandler<TRequest, TResponse> handler,
    IEnumerable<IPipelineBehavior<TRequest, TResponse>> behaviors)
    where TRequest : IRequest<TResponse>
{
    private readonly IPipelineBehavior<TRequest, TResponse>[] _behaviors = [.. behaviors];

    /// <summary>Runs <paramref name="request"/> through every behavior and then the handler.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The response produced by the pipeline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RequestHandlerContinuation<TResponse> next = () => handler.Handle(request, cancellationToken);

        for (var index = _behaviors.Length - 1; index >= 0; index--)
        {
            var behavior = _behaviors[index];
            var inner = next;
            next = () => behavior.Handle(request, inner, cancellationToken);
        }

        return next();
    }
}
