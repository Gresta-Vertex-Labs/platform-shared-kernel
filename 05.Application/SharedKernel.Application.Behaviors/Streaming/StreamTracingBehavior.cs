using MediatR;
using SharedKernel.Application.Behaviors.Metrics;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SharedKernel.Application.Behaviors.Streaming;

/// <summary>
/// Starts a distributed-tracing <see cref="Activity"/> spanning the lifetime of a streaming
/// request — from stream-open until all items are consumed or an exception terminates the stream.
/// </summary>
/// <typeparam name="TRequest">The streaming request type.</typeparam>
/// <typeparam name="TResponse">The per-item payload type yielded by the stream.</typeparam>
/// <remarks>
/// <c>ActivitySource.StartActivity</c> returns <see langword="null"/> when no listener is
/// registered — all <c>activity?.</c> accesses are safe no-ops in that case (zero allocation cost
/// when tracing is not collected). The <c>request.name</c> tag uses
/// <c>typeof(TRequest).FullName ?? typeof(TRequest).Name</c> to prevent collisions when two
/// assemblies define a stream request with the same short name.
/// </remarks>
public sealed class StreamTracingBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    /// <inheritdoc/>
    public async IAsyncEnumerable<TResponse> Handle(
        TRequest request,
        StreamHandlerDelegate<TResponse> next,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity("Stream.Handle");
        activity?.SetTag("request.name", typeof(TRequest).FullName ?? typeof(TRequest).Name);

        await foreach (var item in next().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }
}
