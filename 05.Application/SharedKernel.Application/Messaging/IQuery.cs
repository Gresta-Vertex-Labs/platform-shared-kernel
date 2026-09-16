using MediatR;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Messaging;

/// <summary>
/// Represents a read-only query that returns a payload of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">The unwrapped payload type returned on success.</typeparam>
/// <remarks>
/// Implements <see cref="IQueryBase"/>, never <see cref="ICommandBase"/> — command-stage behaviors in
/// <c>SharedKernel.Application.Behaviors</c> (transaction, idempotency, auditing) never apply to
/// queries. Queries that want automatic caching additionally implement
/// <c>ICacheableQuery&lt;TResponse&gt;</c> (<c>SharedKernel.Application.Behaviors.Caching</c>) —
/// typically as <c>ICacheableQuery&lt;Result&lt;TResponse&gt;&gt;</c>, matching the same
/// <see cref="IRequest{TResponse}"/> contract this interface already implements.
/// </remarks>
public interface IQuery<TResponse> : IQueryBase, IRequest<Result<TResponse>>;
