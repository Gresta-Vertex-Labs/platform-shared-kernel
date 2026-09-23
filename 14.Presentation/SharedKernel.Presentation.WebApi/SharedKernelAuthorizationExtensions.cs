using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Registers the authorization behind <see cref="RequirePermissionAttribute"/> and its siblings.</summary>
public static class SharedKernelAuthorizationExtensions
{
    /// <summary>
    /// Registers ASP.NET Core authorization with the policies behind <see cref="RequirePermissionAttribute"/>,
    /// <see cref="RequireRoleAttribute"/>, <see cref="RequireFreshAuthenticationAttribute"/> and
    /// <see cref="RequireAuthenticationMethodAttribute"/>, and the problem responses for refused requests.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    /// <remarks>
    /// <para>
    /// Called by <c>AddSharedKernelWebApi()</c>; call it yourself only in a host that does not use that, such as a
    /// gRPC-only service. Safe to call more than once and in any order with <c>AddAuthorization()</c>: a later
    /// <c>AddAuthorization()</c> does not replace the policy provider, and policies a service registers by name keep
    /// working.
    /// </para>
    /// <para>
    /// Callers are evaluated through their <see cref="Security.Abstractions.IUserContext"/>, so the authentication
    /// package in use must register its <see cref="Security.Abstractions.IUserContextMapper"/> (the SharedKernel OIDC,
    /// API key and mTLS packages do). An <c>IClock</c> is registered when none is.
    /// </para>
    /// <para>
    /// Registering your own <see cref="IAuthorizationPolicyProvider"/> or <see cref="IAuthorizationMiddlewareResultHandler"/>
    /// afterwards replaces this package's, and with it the attributes' policies or the problem bodies.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(SharedKernelAuthorizationMarker)))
        {
            return services;
        }

        services.AddSingleton<SharedKernelAuthorizationMarker>();
        services.AddAuthorization();
        services.AddClock();

        // Add, not TryAdd: AddAuthorization() only TryAdds the default provider and result handler, so these win
        // whether AddAuthorization() runs before or after this call.
        services.AddSingleton<IAuthorizationPolicyProvider, SharedKernelAuthorizationPolicyProvider>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, SharedKernelAuthorizationResultHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IAuthorizationHandler, SharedKernelRequirementHandler>());

        return services;
    }

    private sealed class SharedKernelAuthorizationMarker;
}
