namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Declares that an endpoint requires the caller to hold at least one of the specified
/// permissions.
/// </summary>
/// <remarks>
/// <para>
/// Same shape and composition semantics as <see cref="RequireRoleAttribute"/>, evaluated by
/// <see cref="AuthorizationRequirementEndpointFilter"/> against
/// <see cref="SharedKernel.Security.Abstractions.IUserContext.HasPermission"/>
/// instead of <c>HasRole</c>.
/// </para>
/// <para>
/// Permissions listed within <b>one</b> attribute instance are OR'd — the caller needs any one
/// of them. Stacking multiple <see cref="RequireRoleAttribute"/>/<see cref="RequirePermissionAttribute"/>
/// instances on the same endpoint is AND'd — the caller must satisfy every attached attribute.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequirePermissionAttribute"/> class.
    /// </summary>
    /// <param name="permissions">
    /// The set of permissions, any one of which satisfies this attribute instance (OR semantics).
    /// </param>
    public RequirePermissionAttribute(params string[] permissions)
    {
        Permissions = permissions;
    }

    /// <summary>
    /// Gets the permissions, any one of which satisfies this attribute instance.
    /// </summary>
    public IReadOnlyCollection<string> Permissions { get; }
}
