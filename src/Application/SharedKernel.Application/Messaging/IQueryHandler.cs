using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Messaging;

/// <summary>
/// Handles a query of type <typeparamref name="TQuery"/>.
/// </summary>
/// <typeparam name="TQuery">The query type, constrained to <see cref="IQuery{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The unwrapped payload type returned on success.</typeparam>
/// <remarks>
/// A pure alias over <see cref="IRequestHandler{TRequest,TResponse}"/> with zero added
/// members — see <see cref="ICommandHandler{TCommand}"/> remarks for the full rationale.
/// </remarks>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
