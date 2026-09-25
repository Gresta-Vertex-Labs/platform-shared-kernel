namespace SharedKernel.Application.Authorization;

/// <summary>
/// Controls how <see cref="IAuthorizeRequest.RequiredPermissions"/> is evaluated by
/// <c>AuthorizationBehavior</c> (<c>SharedKernel.Application.Pipeline</c>).
/// </summary>
public enum PermissionMatch
{
    /// <summary>Every declared permission must be held by the caller (AND composition).</summary>
    All,

    /// <summary>At least one declared permission must be held by the caller (OR composition).</summary>
    Any,
}
