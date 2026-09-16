namespace SharedKernel.Application.Messaging;

/// <summary>
/// Zero-member marker interface implemented by <see cref="IQuery{TResponse}"/>.
/// </summary>
/// <remarks>
/// The read-side counterpart to <see cref="ICommandBase"/> — a pure generic-constraint marker that
/// lets pipeline plumbing and diagnostics (e.g. the <c>request.kind</c> telemetry tag in
/// <c>SharedKernel.Application.Behaviors</c>) recognize "any query shape" regardless of the query's
/// response type. Never implemented by <see cref="ICommand"/> or <see cref="ICommand{TResponse}"/>.
/// </remarks>
public interface IQueryBase;
