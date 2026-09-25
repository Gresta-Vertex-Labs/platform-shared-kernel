namespace SharedKernel.Application.Authorization;

/// <summary>
/// Marks a command or query as requiring an authorization check before its handler runs.
/// </summary>
/// <remarks>
/// Requests that do not implement this marker skip
/// <c>AuthorizationBehavior</c> (<c>SharedKernel.Application.Pipeline</c>) entirely — a DI/runtime
/// fact (the behavior never resolves into that request's pipeline), never a config flag or an
/// if-check inside the behavior. Can be implemented by both commands and queries — authorization is
/// not commands-only; a query can require a permission check too (e.g. "view another tenant's
/// data").
/// </remarks>
public interface IAuthorizeRequest
{
    /// <summary>
    /// Gets the permissions the caller must hold for the request to proceed.
    /// </summary>
    /// <remarks>
    /// No default implementation — every implementer must declare its own permission set
    /// explicitly. An empty collection is a valid but deliberately fail-closed declaration: see
    /// <c>AuthorizationBehavior</c> (<c>SharedKernel.Application.Pipeline</c>)'s remarks for why an
    /// empty set denies rather than allows.
    /// </remarks>
    IReadOnlyCollection<string> RequiredPermissions { get; }

    /// <summary>
    /// Gets how <see cref="RequiredPermissions"/> is evaluated.
    /// </summary>
    /// <value>Defaults to <see cref="PermissionMatch.All"/>.</value>
    PermissionMatch PermissionMatch => PermissionMatch.All;
}
