namespace SharedKernel.Application.Streaming;

/// <summary>
/// Handles a streaming query of type <typeparamref name="TQuery"/>.
/// </summary>
/// <typeparam name="TQuery">The streaming query type, constrained to <see cref="IStreamQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The raw per-item payload type.</typeparam>
/// <remarks>
/// A handler returns the items lazily; enumeration starts when the caller enumerates the stream
/// returned by <see cref="Messaging.ISender.CreateStream{TResponse}(IStreamQuery{TResponse}, CancellationToken)"/>.
/// </remarks>
public interface IStreamQueryHandler<in TQuery, out TResponse>
    where TQuery : IStreamQuery<TResponse>
{
    /// <summary>Produces the items of <paramref name="request"/>.</summary>
    /// <param name="request">The streaming query.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The items, in order.</returns>
    IAsyncEnumerable<TResponse> Handle(TQuery request, CancellationToken cancellationToken);
}
