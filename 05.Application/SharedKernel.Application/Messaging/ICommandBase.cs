namespace SharedKernel.Application.Messaging;

/// <summary>
/// Zero-member marker interface implemented by both <see cref="ICommand"/> and
/// <see cref="ICommand{TResponse}"/>.
/// </summary>
/// <remarks>
/// This is a pure generic-constraint marker — it exists so that pipeline behaviors in
/// <c>SharedKernel.Application.Behaviors</c> (e.g. <c>TransactionBehavior&lt;TRequest,TResponse&gt;</c>,
/// <c>IdempotentCommandBehavior&lt;TRequest,TResponse&gt;</c>) can constrain on "any command shape"
/// without needing two separate behavior implementations for <see cref="ICommand"/> and
/// <see cref="ICommand{TResponse}"/>. <see cref="IQuery{TResponse}"/> never implements this interface.
/// </remarks>
public interface ICommandBase;
