namespace SharedKernel.Presentation.Authorization;

/// <summary>
/// Declares that an endpoint requires the caller's authentication to be no older than
/// <see cref="MaxAge"/> — a step-up/fresh-authentication gate distinct from role/permission checks.
/// </summary>
/// <remarks>
/// <para>
/// Usable directly on an MVC controller/action or attached to a Minimal API endpoint via
/// <c>RouteHandlerBuilder.WithMetadata(new RequireFreshAuthenticationAttribute(...))</c> — see
/// <c>AuthorizationEndpointFilterExtensions.RequireFreshAuthentication</c> (<c>SharedKernel.Presentation.WebApi</c>)
/// for the equivalent Minimal API sugar.
/// </para>
/// <para>
/// Evaluated by <c>AuthorizationRequirementEndpointFilter</c> (HTTP) and <c>GrpcAuthorizationInterceptor</c> (gRPC) — the same global filter that
/// evaluates <see cref="RequireRoleAttribute"/>/<see cref="RequirePermissionAttribute"/> — against
/// <c>IUserContext.IsAuthenticationFresherThan</c>.
/// Rejects a request whose <c>AuthTime</c> is older than <see cref="MaxAge"/> or absent with
/// <see cref="SharedKernel.Primitives.Errors.Error.Forbidden(string, string)"/> (403) — the same
/// rejection shape <see cref="RequireRoleAttribute"/>/<see cref="RequirePermissionAttribute"/>
/// already use. An anonymous/unauthenticated caller is rejected via the ordinary absent-
/// <c>AuthTime</c> path, with no dedicated <c>IsAuthenticated</c> branch. Composes AND-across with
/// every other attribute this filter evaluates.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireFreshAuthenticationAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequireFreshAuthenticationAttribute"/> class.
    /// </summary>
    /// <param name="maxAgeSeconds">
    /// The maximum acceptable age, in seconds, of the caller's authentication event. Must be
    /// greater than zero.
    /// </param>
    public RequireFreshAuthenticationAttribute(int maxAgeSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxAgeSeconds, 0);
        MaxAge = TimeSpan.FromSeconds(maxAgeSeconds);
    }

    /// <summary>Gets the maximum acceptable age of the caller's authentication event.</summary>
    public TimeSpan MaxAge { get; }
}
