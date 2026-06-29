using MediatR;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Messaging;

/// <summary>
/// Represents a read-only query that returns a payload of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">The unwrapped payload type returned on success.</typeparam>
/// <remarks>
/// Does <b>not</b> implement <see cref="ICommandBase"/> — <c>TransactionBehavior</c>
/// (<c>SharedKernel.Application.Behaviors</c>) never applies to queries. Queries that want automatic
/// caching additionally implement <c>ICacheableQuery&lt;TResponse&gt;</c>
/// (<c>SharedKernel.Application.Behaviors</c>) — typically as
/// <c>ICacheableQuery&lt;Result&lt;TResponse&gt;&gt;</c>, matching the same
/// <see cref="IRequest{TResponse}"/> contract this interface already implements.
/// </remarks>
public interface IQuery<TResponse> : IRequest<Result<TResponse>>;
