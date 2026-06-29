using MediatR;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Messaging;

/// <summary>
/// Handles a void-returning command of type <typeparamref name="TCommand"/>.
/// </summary>
/// <typeparam name="TCommand">The command type, constrained to <see cref="ICommand"/>.</typeparam>
/// <remarks>
/// A pure alias over MediatR's <see cref="IRequestHandler{TRequest,TResponse}"/> with zero added
/// members — it exists so handler classes self-document their CQRS role in the class declaration
/// (<c>public sealed class DeleteOrderCommandHandler : ICommandHandler&lt;DeleteOrderCommand&gt;</c>)
/// instead of the less informative <c>IRequestHandler&lt;DeleteOrderCommand, Result&gt;</c>.
/// Handlers must never return <c>Envelope</c>/<c>Envelope&lt;T&gt;</c> — this layer returns
/// <see cref="Result"/>/<see cref="Result{T}"/> exclusively.
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
