namespace SharedKernel.Application.Behaviors.Authorization;

/// <summary>
/// Marks a command or query as requiring an authorization check before its handler runs.
/// </summary>
/// <remarks>
/// The requirement string is whatever minimal shape (permission/policy name) the consuming
/// service's <see cref="IAuthorizationContext"/> bridge understands. Requests that do not
/// implement this marker skip <see cref="Authorization.AuthorizationBehavior{TRequest,TResponse}"/>
/// entirely — a DI/runtime fact (the behavior never resolves into that request's pipeline), not a
/// config flag or an if-check inside the behavior. Can be implemented by both commands and
/// queries — unlike Transaction/Idempotency, authorization is not commands-only; queries can
/// require permission checks too (e.g. "view another tenant's data").
/// </remarks>
public interface IAuthorizeRequest
{
    /// <summary>Gets the opaque authorization requirement string for this request.</summary>
    string Requirement { get; }
}
