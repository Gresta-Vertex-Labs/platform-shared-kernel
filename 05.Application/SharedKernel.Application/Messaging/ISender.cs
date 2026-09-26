using SharedKernel.Application.Streaming;

namespace SharedKernel.Application.Messaging;

/// <summary>
/// Sends a request through its pipeline to its handler, or opens a streaming query.
/// </summary>
/// <remarks>
/// <para>
/// The only dispatch contract application code depends on. <c>AddSharedKernelApplication(..., app =&gt; app.UseMediatR())</c>
/// (<c>SharedKernel.Application.Mediator.MediatR</c>) registers the shipped implementation; a
/// different mediator is a different implementation of this interface, with no change to requests,
/// handlers or behaviors.
/// </para>
/// <para>
/// A request sent from inside a handler runs in the same DI scope as the request that sent it, so
/// scoped state — the command scope, the unit of work's transaction — is shared by the nested call.
/// </para>
/// </remarks>
public interface ISender
{
    /// <summary>Sends <paramref name="request"/> through the request pipeline to its handler.</summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The response produced by the pipeline.</returns>
    /// <exception cref="InvalidOperationException">No handler is registered for the request's type.</exception>
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>Opens <paramref name="request"/> through the stream pipeline to its handler.</summary>
    /// <typeparam name="TResponse">The item type.</typeparam>
    /// <param name="request">The streaming query.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The items; nothing runs until the caller enumerates them.</returns>
    /// <exception cref="InvalidOperationException">No handler is registered for the query's type.</exception>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamQuery<TResponse> request,
        CancellationToken cancellationToken = default);
}
