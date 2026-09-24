using MediatR;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application;

/// <summary>
/// Represents a void-returning command — a request that mutates state and reports success or
/// failure without a payload.
/// </summary>
/// <remarks>
/// Binds the handler's response type to the non-generic <see cref="Result"/>
/// (<c>SharedKernel.Primitives</c>) rather than <see langword="void"/>, so callers can branch on
/// <c>IsSuccess</c>/<c>IsFailure</c> without relying on an exception.
/// </remarks>
public interface ICommand : ICommandBase, IRequest<Result>;
