using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline;
using SharedKernel.Application.Streaming;

namespace SharedKernel.Application.Mediator.MediatR;

/// <summary>Carries a kernel request through MediatR.</summary>
internal sealed record RequestEnvelope<TRequest, TResponse>(TRequest Request) : global::MediatR.IRequest<TResponse>
    where TRequest : IRequest<TResponse>;

/// <summary>The MediatR handler for one kernel request type: runs the kernel pipeline.</summary>
internal sealed class RequestEnvelopeHandler<TRequest, TResponse>(RequestPipeline<TRequest, TResponse> pipeline)
    : global::MediatR.IRequestHandler<RequestEnvelope<TRequest, TResponse>, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Handle(RequestEnvelope<TRequest, TResponse> request, CancellationToken cancellationToken)
        => pipeline.HandleAsync(request.Request, cancellationToken);
}

/// <summary>Carries a kernel streaming query through MediatR.</summary>
internal sealed record StreamEnvelope<TRequest, TResponse>(TRequest Request) : global::MediatR.IStreamRequest<TResponse>
    where TRequest : IStreamQuery<TResponse>;

/// <summary>The MediatR stream handler for one kernel streaming query type: runs the kernel stream pipeline.</summary>
internal sealed class StreamEnvelopeHandler<TRequest, TResponse>(StreamRequestPipeline<TRequest, TResponse> pipeline)
    : global::MediatR.IStreamRequestHandler<StreamEnvelope<TRequest, TResponse>, TResponse>
    where TRequest : IStreamQuery<TResponse>
{
    public IAsyncEnumerable<TResponse> Handle(StreamEnvelope<TRequest, TResponse> request, CancellationToken cancellationToken)
        => pipeline.Handle(request.Request, cancellationToken);
}

/// <summary>
/// Wraps a request of a type known only at runtime in its closed envelope. One instance per request
/// type, registered at startup as a keyed singleton whose key is the request type.
/// </summary>
internal abstract class RequestDispatcher<TResponse>
{
    public abstract Task<TResponse> SendAsync(
        IRequest<TResponse> request,
        global::MediatR.ISender mediator,
        CancellationToken cancellationToken);
}

/// <summary>The closed dispatcher for <typeparamref name="TRequest"/>.</summary>
internal sealed class RequestDispatcher<TRequest, TResponse> : RequestDispatcher<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override Task<TResponse> SendAsync(
        IRequest<TResponse> request,
        global::MediatR.ISender mediator,
        CancellationToken cancellationToken)
        => mediator.Send(new RequestEnvelope<TRequest, TResponse>((TRequest)request), cancellationToken);
}

/// <summary>The stream counterpart of <see cref="RequestDispatcher{TResponse}"/>.</summary>
internal abstract class StreamDispatcher<TResponse>
{
    public abstract IAsyncEnumerable<TResponse> CreateStream(
        IStreamQuery<TResponse> request,
        global::MediatR.ISender mediator,
        CancellationToken cancellationToken);
}

/// <summary>The closed stream dispatcher for <typeparamref name="TRequest"/>.</summary>
internal sealed class StreamDispatcher<TRequest, TResponse> : StreamDispatcher<TResponse>
    where TRequest : IStreamQuery<TResponse>
{
    public override IAsyncEnumerable<TResponse> CreateStream(
        IStreamQuery<TResponse> request,
        global::MediatR.ISender mediator,
        CancellationToken cancellationToken)
        => mediator.CreateStream(new StreamEnvelope<TRequest, TResponse>((TRequest)request), cancellationToken);
}
