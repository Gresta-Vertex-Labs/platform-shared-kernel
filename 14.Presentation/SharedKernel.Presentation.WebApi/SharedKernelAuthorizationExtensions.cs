using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Presentation.WebApi.Startup;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Registers the authorization behind <see cref="RequirePermissionAttribute"/> and its siblings.</summary>
internal static class SharedKernelAuthorizationExtensions
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
    /// gRPC-only service. Safe to call more than once and in any order with <c>AddAuthorization()</c>: policies a
    /// service registers by name keep working.
    /// </para>
    /// <para>
    /// Callers are evaluated through their <see cref="Security.Abstractions.IUserContext"/>, so the authentication
    /// package in use must register its <see cref="Security.Abstractions.IUserContextMapper"/> (the SharedKernel OIDC,
    /// API key and mTLS packages do); a scheme without one is named in a warning when the host starts. An
    /// <c>IClock</c> is registered when none is.
    /// </para>
    /// <para>
    /// A service's own <see cref="IAuthorizationPolicyProvider"/> or <see cref="IAuthorizationMiddlewareResultHandler"/>
    /// registered <b>before</b> this call is decorated: the platform answers its own policy names and refusals and
    /// passes everything else to the service's implementation. One registered <b>after</b> this call would replace
    /// the platform's, so the host refuses to start with an <see cref="InvalidOperationException"/> naming it.
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

        // TryAdds the framework defaults, so after this call exactly the implementation that should be decorated is
        // registered last: the service's own when it registered one first, otherwise the framework's.
        services.AddAuthorization();
        services.AddClock();

        ServiceDecoration.Decorate<IAuthorizationPolicyProvider>(
            services,
            static (_, inner) => new SharedKernelAuthorizationPolicyProvider(inner));
        ServiceDecoration.Decorate<IAuthorizationMiddlewareResultHandler>(
            services,
            static (provider, inner) => new SharedKernelAuthorizationResultHandler(
                inner,
                provider.GetService<ILogger<SharedKernelAuthorizationResultHandler>>()));

        services.TryAddEnumerable(ServiceDescriptor.Transient<IAuthorizationHandler, SharedKernelRequirementHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SharedKernelAuthorizationStartupCheck>());

        return services;
    }

    private sealed class SharedKernelAuthorizationMarker;
}
