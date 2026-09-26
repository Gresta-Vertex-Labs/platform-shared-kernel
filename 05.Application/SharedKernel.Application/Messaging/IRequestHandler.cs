namespace SharedKernel.Application.Messaging;

/// <summary>
/// Handles a request of type <typeparamref name="TRequest"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// Application code normally implements one of the role-named aliases instead —
/// <see cref="ICommandHandler{TCommand}"/>, <see cref="ICommandHandler{TCommand,TResponse}"/> or
/// <see cref="IQueryHandler{TQuery,TResponse}"/> — so the class declaration states its CQRS role.
/// </remarks>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Handles <paramref name="request"/>.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The response.</returns>
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
