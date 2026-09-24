using Microsoft.AspNetCore.Builder;
using SharedKernel.Presentation.WebApi.Authorization;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Declares authorization requirements on endpoints: route handlers, groups, <c>MapControllers()</c>,
/// <c>MapHub&lt;T&gt;()</c> and <c>MapGrpcService&lt;T&gt;()</c> alike.
/// </summary>
/// <remarks>
/// Each call adds a native ASP.NET Core authorization policy, evaluated by <c>UseAuthorization()</c> against the
/// caller's <see cref="Security.Abstractions.IUserContext"/>. Values within one call are alternatives (OR); several
/// calls must all be satisfied (AND). An anonymous caller is answered 401; see the attributes for the other answers.
/// </remarks>
public static class AuthorizationConventionExtensions
{
    /// <summary>Requires the caller to hold at least one of <paramref name="permissions"/>.</summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint, group or mapping.</param>
    /// <param name="permissions">The permissions, any one of which is enough.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    public static TBuilder RequireEndpointPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.RequireAuthorization(new RequireEndpointPermissionAttribute(permissions));
    }

    /// <summary>Requires the caller to hold at least one of <paramref name="roles"/>.</summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint, group or mapping.</param>
    /// <param name="roles">The roles, any one of which is enough.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    public static TBuilder RequireRole<TBuilder>(this TBuilder builder, params string[] roles)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.RequireAuthorization(new RequireRoleAttribute(roles));
    }

    /// <summary>
    /// Requires the caller to have authenticated no more than <paramref name="maxAgeSeconds"/> seconds ago; an older
    /// authentication is answered 401 with an RFC 9470 step-up challenge.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint, group or mapping.</param>
    /// <param name="maxAgeSeconds">The oldest acceptable authentication, in seconds; greater than zero.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    public static TBuilder RequireFreshAuthentication<TBuilder>(this TBuilder builder, int maxAgeSeconds)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.RequireAuthorization(new RequireFreshAuthenticationAttribute(maxAgeSeconds));
    }

    /// <summary>
    /// Requires the caller to have authenticated no longer than <paramref name="maxAge"/> ago; an older
    /// authentication is answered 401 with an RFC 9470 step-up challenge.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint, group or mapping.</param>
    /// <param name="maxAge">The oldest acceptable authentication; at least one second. Fractions of a second are dropped.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    public static TBuilder RequireFreshAuthentication<TBuilder>(this TBuilder builder, TimeSpan maxAge)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAge, TimeSpan.FromSeconds(1));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxAge, TimeSpan.FromSeconds(int.MaxValue));

        return builder.RequireFreshAuthentication((int)maxAge.TotalSeconds);
    }

    /// <summary>Requires the caller to have authenticated with at least one of <paramref name="methods"/> (<c>amr</c> values).</summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint, group or mapping.</param>
    /// <param name="methods">The authentication methods, any one of which is enough.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    /// <remarks>
    /// A method counts for as long as the caller's principal carries it. For a step-up method such as <c>otp</c>, prefer
    /// the overload with a maximum age.
    /// </remarks>
    public static TBuilder RequireAuthenticationMethod<TBuilder>(this TBuilder builder, params string[] methods)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.RequireAuthorization(new RequireAuthenticationMethodAttribute(methods));
    }

    /// <summary>
    /// Requires the caller to have verified at least one of <paramref name="methods"/> (<c>amr</c> values) no longer
    /// than <paramref name="maxAge"/> ago; an older method, or one without a known time, is answered 401 with an
    /// RFC 9470 step-up challenge carrying <c>max_age</c>.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint, group or mapping.</param>
    /// <param name="maxAge">
    /// How long ago one of the methods may have been verified; at least one second. Fractions of a second are dropped.
    /// </param>
    /// <param name="methods">The authentication methods, any one of which is enough.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    /// <remarks>
    /// The method's time is compared with the clock each time the requirement is evaluated. On <c>MapHub&lt;T&gt;()</c>
    /// a convention guards only opening the connection; to end a step-up on a connection that stays open, put
    /// <see cref="RequireAuthenticationMethodAttribute"/> with <see cref="RequireAuthenticationMethodAttribute.MaxAgeSeconds"/>
    /// on the hub method, which SignalR authorizes on every call.
    /// </remarks>
    public static TBuilder RequireAuthenticationMethod<TBuilder>(this TBuilder builder, TimeSpan maxAge, params string[] methods)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAge, TimeSpan.FromSeconds(1));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxAge, TimeSpan.FromSeconds(int.MaxValue));

        return builder.RequireAuthorization(new RequireAuthenticationMethodAttribute(methods) { MaxAgeSeconds = (int)maxAge.TotalSeconds });
    }
}
