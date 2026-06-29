using MediatR;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Messaging;

/// <summary>
/// Represents a value-returning command — a request that mutates state and reports a payload of
/// type <typeparamref name="TResponse"/> on success.
/// </summary>
/// <typeparam name="TResponse">
/// The unwrapped payload type (e.g. <see cref="Guid"/> for a "create" command returning a new ID).
/// Never wrap <typeparamref name="TResponse"/> in <see cref="Result{T}"/> yourself when declaring
/// the command — the handler's return type does that for you.
/// </typeparam>
public interface ICommand<TResponse> : ICommandBase, IRequest<Result<TResponse>>;
