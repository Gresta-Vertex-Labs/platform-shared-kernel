using MediatR;

namespace SharedKernel.Application;

/// <summary>
/// Handles a streaming query of type <typeparamref name="TQuery"/>.
/// </summary>
/// <typeparam name="TQuery">The streaming query type, constrained to <see cref="IStreamQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The raw per-item payload type.</typeparam>
/// <remarks>
/// A pure alias over MediatR's <see cref="IStreamRequestHandler{TRequest,TResponse}"/> with zero
/// added members — exactly mirroring how <c>IQueryHandler&lt;,&gt;</c> aliases
/// <see cref="IRequestHandler{TRequest,TResponse}"/>. A handler implements
/// <c>IAsyncEnumerable&lt;TResponse&gt; Handle(TQuery request, CancellationToken ct)</c> and
/// self-documents its streaming-query role instead of the less informative
/// <see cref="IStreamRequestHandler{TRequest,TResponse}"/>.
/// </remarks>
public interface IStreamQueryHandler<TQuery, TResponse> : IStreamRequestHandler<TQuery, TResponse>
    where TQuery : IStreamQuery<TResponse>;
