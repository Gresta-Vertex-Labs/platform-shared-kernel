using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Application.Context;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>Registers the <c>12.Security</c>-backed <see cref="IRequestContext"/>.</summary>
public static class RequestContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SecurityRequestContext"/> as the scoped <see cref="IRequestContext"/>, and
    /// <see cref="UserContextTenantProvider"/> as <see cref="ITenantProvider"/> unless one is already
    /// registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Registered with <c>Add</c>, not <c>TryAdd</c>, so it replaces the fail-closed
    /// <see cref="AnonymousRequestContext"/> default that <c>SharedKernel.Persistence.EfCore</c>'s
    /// <c>Build()</c> registers, whichever of the two calls comes first. Requires an
    /// <see cref="IUserContext"/> registration (e.g. from <c>AddOidcAuthentication(...)</c>).
    /// </para>
    /// <para>
    /// Satisfies the host-start checks of <c>AddSharedKernelApplication</c> for <c>[RequirePermission]</c> use
    /// cases and <c>WithIdempotency()</c>; it may be called before or after that registration.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelRequestContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ITenantProvider, UserContextTenantProvider>();
        services.AddScoped<IRequestContext, SecurityRequestContext>();

        return services;
    }
}
