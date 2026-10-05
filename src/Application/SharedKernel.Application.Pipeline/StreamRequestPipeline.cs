using SharedKernel.Application.Streaming;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// Runs one streaming query through its stream behaviors and then its handler.
/// </summary>
/// <typeparam name="TRequest">The streaming query type.</typeparam>
/// <typeparam name="TResponse">The item type.</typeparam>
/// <remarks>
/// The stream counterpart of <see cref="RequestPipeline{TRequest,TResponse}"/>. Only
/// <see cref="IStreamPipelineBehavior{TRequest,TResponse}"/> registrations apply, in registration
/// order, the first one outermost; request behaviors never do. This package registers no stream
/// behavior of its own.
/// </remarks>
/// <param name="handler">The query's handler.</param>
/// <param name="behaviors">The stream behaviors applicable to the query, outermost first.</param>
public sealed class StreamRequestPipeline<TRequest, TResponse>(
    IStreamQueryHandler<TRequest, TResponse> handler,
    IEnumerable<IStreamPipelineBehavior<TRequest, TResponse>> behaviors)
    where TRequest : IStreamQuery<TResponse>
{
    private readonly IStreamPipelineBehavior<TRequest, TResponse>[] _behaviors = [.. behaviors];

    /// <summary>Opens <paramref name="request"/> through every stream behavior and then the handler.</summary>
    /// <param name="request">The streaming query.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The items produced by the pipeline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        StreamHandlerContinuation<TResponse> next = () => handler.Handle(request, cancellationToken);

        for (var index = _behaviors.Length - 1; index >= 0; index--)
        {
            var behavior = _behaviors[index];
            var inner = next;
            next = () => behavior.Handle(request, inner, cancellationToken);
        }

        return next();
    }
}
