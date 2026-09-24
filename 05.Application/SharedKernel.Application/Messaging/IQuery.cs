using MediatR;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application;

/// <summary>
/// Represents a read-only query that returns a payload of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TResponse">The unwrapped payload type returned on success.</typeparam>
/// <remarks>
/// Implements <see cref="IQueryBase"/>, never <see cref="ICommandBase"/> — command-stage behaviors in
/// <c>SharedKernel.Application</c> (transaction, idempotency, auditing) never apply to
/// queries. A query that wants automatic caching implements
/// <c>ICacheableQuery&lt;TResponse&gt;</c> (<c>SharedKernel.Application.Caching</c>) instead,
/// which is itself an <see cref="IQuery{TResponse}"/>.
/// </remarks>
public interface IQuery<TResponse> : IQueryBase, IRequest<Result<TResponse>>;
