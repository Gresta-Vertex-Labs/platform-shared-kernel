using MediatR;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application;

/// <summary>
/// Handles a void-returning command of type <typeparamref name="TCommand"/>.
/// </summary>
/// <typeparam name="TCommand">The command type, constrained to <see cref="ICommand"/>.</typeparam>
/// <remarks>
/// A pure alias over MediatR's <see cref="IRequestHandler{TRequest,TResponse}"/> with zero added
/// members — it exists so handler classes self-document their CQRS role in the class declaration
/// (<c>public sealed class DeleteOrderCommandHandler : ICommandHandler&lt;DeleteOrderCommand&gt;</c>)
/// instead of the less informative <c>IRequestHandler&lt;DeleteOrderCommand, Result&gt;</c>.
/// Handlers return <see cref="Result"/>/<see cref="Result{T}"/> exclusively and never shape a wire
/// response: the HTTP boundary maps a failure to RFC 9457 ProblemDetails through
/// <c>ResultHttpExtensions</c>, and the REST client maps it back with <c>ReadResultAsync</c>.
/// </remarks>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

/// <summary>
/// Handles a value-returning command of type <typeparamref name="TCommand"/>.
/// </summary>
/// <typeparam name="TCommand">The command type, constrained to <see cref="ICommand{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The unwrapped payload type returned on success.</typeparam>
/// <remarks>
/// A pure alias over MediatR's <see cref="IRequestHandler{TRequest,TResponse}"/> with zero added
/// members — see <see cref="ICommandHandler{TCommand}"/> remarks for the full rationale.
/// </remarks>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, Result<TResponse>>
    where TCommand : ICommand<TResponse>;
