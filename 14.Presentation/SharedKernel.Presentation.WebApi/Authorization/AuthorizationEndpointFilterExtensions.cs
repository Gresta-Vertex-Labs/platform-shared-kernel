using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Minimal API sugar for attaching <see cref="RequireRoleAttribute"/>/
/// <see cref="RequirePermissionAttribute"/> metadata, plus the DI registration for
/// <see cref="AuthorizationRequirementEndpointFilter"/>.
/// </summary>
public static class AuthorizationEndpointFilterExtensions
{
    /// <summary>
    /// Attaches a <see cref="RequireRoleAttribute"/> to the endpoint produced by
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The route handler builder to attach metadata to.</param>
    /// <param name="roles">
    /// The set of roles, any one of which satisfies this attribute instance (OR semantics).
    /// </param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <remarks>
    /// Purely metadata attachment — this does not itself perform the authorization check or
    /// register <see cref="AuthorizationRequirementEndpointFilter"/> on the pipeline. The
    /// consumer must additionally call
    /// <c>.AddEndpointFilter&lt;AuthorizationRequirementEndpointFilter&gt;()</c> on
    /// <c>MapControllers()</c> and/or each route group — see <see cref="AddSharedKernelAuthorizationFilters"/>.
    /// </remarks>
    public static RouteHandlerBuilder RequireRole(this RouteHandlerBuilder builder, params string[] roles)
        => builder.WithMetadata(new RequireRoleAttribute(roles));

    /// <summary>
    /// Attaches a <see cref="RequireRoleAttribute"/> to every endpoint produced by
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The route group builder to attach metadata to.</param>
    /// <param name="roles">
    /// The set of roles, any one of which satisfies this attribute instance (OR semantics).
    /// </param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static RouteGroupBuilder RequireRole(this RouteGroupBuilder builder, params string[] roles)
        => builder.WithMetadata(new RequireRoleAttribute(roles));

    /// <summary>
    /// Attaches a <see cref="RequirePermissionAttribute"/> to the endpoint produced by
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The route handler builder to attach metadata to.</param>
    /// <param name="permissions">
    /// The set of permissions, any one of which satisfies this attribute instance (OR semantics).
    /// </param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, params string[] permissions)
        => builder.WithMetadata(new RequirePermissionAttribute(permissions));

    /// <summary>
    /// Attaches a <see cref="RequirePermissionAttribute"/> to every endpoint produced by
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The route group builder to attach metadata to.</param>
    /// <param name="permissions">
    /// The set of permissions, any one of which satisfies this attribute instance (OR semantics).
    /// </param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static RouteGroupBuilder RequirePermission(this RouteGroupBuilder builder, params string[] permissions)
        => builder.WithMetadata(new RequirePermissionAttribute(permissions));

    /// <summary>
    /// Attaches a <see cref="RequireFreshAuthenticationAttribute"/> to the endpoint produced by
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The route handler builder to attach metadata to.</param>
    /// <param name="maxAgeSeconds">The maximum acceptable age, in seconds, of the caller's authentication event.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static RouteHandlerBuilder RequireFreshAuthentication(this RouteHandlerBuilder builder, int maxAgeSeconds)
        => builder.WithMetadata(new RequireFreshAuthenticationAttribute(maxAgeSeconds));

    /// <summary>
    /// Attaches a <see cref="RequireFreshAuthenticationAttribute"/> to every endpoint produced by
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The route group builder to attach metadata to.</param>
    /// <param name="maxAgeSeconds">The maximum acceptable age, in seconds, of the caller's authentication event.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static RouteGroupBuilder RequireFreshAuthentication(this RouteGroupBuilder builder, int maxAgeSeconds)
        => builder.WithMetadata(new RequireFreshAuthenticationAttribute(maxAgeSeconds));

    /// <summary>
    /// Attaches a <see cref="RequireAuthenticationMethodAttribute"/> to the endpoint produced by
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The route handler builder to attach metadata to.</param>
    /// <param name="methods">
    /// The set of authentication method references, any one of which satisfies this attribute
    /// (OR semantics).
    /// </param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static RouteHandlerBuilder RequireAuthenticationMethod(this RouteHandlerBuilder builder, params string[] methods)
        => builder.WithMetadata(new RequireAuthenticationMethodAttribute(methods));

    /// <summary>
    /// Attaches a <see cref="RequireAuthenticationMethodAttribute"/> to every endpoint produced by
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The route group builder to attach metadata to.</param>
    /// <param name="methods">
    /// The set of authentication method references, any one of which satisfies this attribute
    /// (OR semantics).
    /// </param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static RouteGroupBuilder RequireAuthenticationMethod(this RouteGroupBuilder builder, params string[] methods)
        => builder.WithMetadata(new RequireAuthenticationMethodAttribute(methods));

    /// <summary>
    /// Registers <see cref="AuthorizationRequirementEndpointFilter"/> as a singleton service.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Mirrors the <c>TenantContextHubFilter</c>/<c>HubExceptionMappingFilter</c> DI-registration
    /// convention in <c>SharedKernel.Presentation.SignalR</c> so the filter can take constructor
    /// dependencies later, even though it is stateless today.
    /// </para>
    /// <para>
    /// <b>This registration alone does not attach the filter to any endpoint.</b> Unlike
    /// <c>AddSharedKernelSignalR</c>'s global hub-filter registration via
    /// <c>HubOptions.AddFilter&lt;T&gt;()</c>, ASP.NET Core's minimal-API/MVC endpoint routing has
    /// no equivalent "apply to every mapped endpoint automatically" hook. The consumer must
    /// additionally call <c>.AddEndpointFilter&lt;AuthorizationRequirementEndpointFilter&gt;()</c>
    /// on <c>MapControllers()</c> and/or each minimal-API route group — this is the one place
    /// this domain's "convention over configuration" philosophy cannot fully deliver a
    /// zero-wiring default.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelAuthorizationFilters(this IServiceCollection services)
    {
        services.AddSingleton<AuthorizationRequirementEndpointFilter>();
        return services;
    }
}
