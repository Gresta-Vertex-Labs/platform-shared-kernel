namespace SharedKernel.Application.Streaming;

/// <summary>
/// Continues a stream pipeline: calling it runs the next stream behavior, or the handler when this
/// is the innermost behavior.
/// </summary>
/// <typeparam name="TResponse">The raw per-item payload type.</typeparam>
/// <returns>The items produced by the rest of the pipeline.</returns>
public delegate IAsyncEnumerable<TResponse> StreamHandlerContinuation<out TResponse>();

/// <summary>
/// Wraps the handling of a streaming query with cross-cutting work.
/// </summary>
/// <typeparam name="TRequest">The streaming query type.</typeparam>
/// <typeparam name="TResponse">The raw per-item payload type.</typeparam>
/// <remarks>
/// Registered open-generic against this interface; the stream pipeline resolves every registration
/// whose generic constraints the query satisfies, in registration order, the first one outermost.
/// A stream behavior is distinct from a request <see cref="Messaging.IPipelineBehavior{TRequest,TResponse}"/>
/// so a behavior written for single responses is never applied to a stream by accident.
/// </remarks>
public interface IStreamPipelineBehavior<in TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Handles <paramref name="request"/>, calling <paramref name="next"/> to continue the pipeline.</summary>
    /// <param name="request">The streaming query.</param>
    /// <param name="next">Continues the pipeline.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The items to return to the caller.</returns>
    IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken);
}
