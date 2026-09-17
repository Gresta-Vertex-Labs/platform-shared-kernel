namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Declares that an endpoint requires the caller to hold at least one of the specified roles.
/// </summary>
/// <remarks>
/// <para>
/// Usable directly on an MVC controller/action — MVC auto-surfaces attributes as endpoint
/// metadata — or attached to a Minimal API endpoint via
/// <c>RouteHandlerBuilder.WithMetadata(new RequireRoleAttribute(...))</c>. See
/// <see cref="AuthorizationEndpointFilterExtensions.RequireRole(Microsoft.AspNetCore.Builder.RouteHandlerBuilder, string[])"/>
/// for the equivalent Minimal API sugar.
/// </para>
/// <para>
/// Roles listed within <b>one</b> attribute instance are OR'd — the caller needs any one of
/// them. Stacking multiple <see cref="RequireRoleAttribute"/>/<see cref="RequirePermissionAttribute"/>
/// instances on the same endpoint is AND'd — the caller must satisfy every attached attribute.
/// This is a fixed, documented composition rule; it is not configurable.
/// </para>
/// <para>
/// Evaluated by <see cref="AuthorizationRequirementEndpointFilter"/> against
/// <see cref="SharedKernel.Security.Abstractions.IUserContext.HasRole"/> — never
/// <see cref="System.Security.Claims.ClaimTypes.Role"/> or the built-in ASP.NET Core
/// <c>[Authorize(Roles = "...")]</c> attribute, which reads <c>ClaimTypes.Role</c> directly and
/// bypasses this platform's claim-mapping-aware role resolution. Role names are compared ordinally (case-sensitive).
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequireRoleAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequireRoleAttribute"/> class.
    /// </summary>
    /// <param name="roles">
    /// The set of roles, any one of which satisfies this attribute instance (OR semantics).
    /// </param>
    public RequireRoleAttribute(params string[] roles)
    {
        Roles = roles;
    }

    /// <summary>
    /// Gets the roles, any one of which satisfies this attribute instance.
    /// </summary>
    public IReadOnlyCollection<string> Roles { get; }
}
