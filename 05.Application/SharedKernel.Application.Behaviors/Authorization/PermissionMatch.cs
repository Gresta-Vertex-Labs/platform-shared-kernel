namespace SharedKernel.Application.Behaviors.Authorization;

/// <summary>
/// Controls how <see cref="IAuthorizeRequest.RequiredPermissions"/> is evaluated by
/// <see cref="AuthorizationBehavior{TRequest,TResponse}"/>.
/// </summary>
public enum PermissionMatch
{
    /// <summary>Every declared permission must be held by the caller (AND composition).</summary>
    All,

    /// <summary>At least one declared permission must be held by the caller (OR composition).</summary>
    Any,
}
