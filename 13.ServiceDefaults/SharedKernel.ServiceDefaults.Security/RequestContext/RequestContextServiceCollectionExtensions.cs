using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Execution.Context;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>Registers the <c>12.Security</c>-backed <see cref="IRequestContext"/>.</summary>
public static class RequestContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRequestContext"/>: the ambient <see cref="RequestContextScope.Current"/> when an inbound
    /// adapter opened one, otherwise the scope's <see cref="SecurityRequestContext"/> over <see cref="IUserContext"/>.
    /// Also registers <see cref="IRequestContextAccessor"/> unless one is already registered.
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
    /// The ambient context wins because the inbound adapters own it: in an HTTP request it is the scope
    /// <see cref="RequestContextApplicationBuilderExtensions.UseSharedKernelRequestContext"/> opens (the caller plus
    /// the request's correlation id), refined by <c>SharedKernel.MultiTenancy</c>'s <c>TenantResolutionMiddleware</c>
    /// when that is used; in a message consumer, workflow activity or scheduled job it is the scope that adapter
    /// opens. <see cref="IRequestContext"/> is therefore registered as transient; the
    /// <see cref="SecurityRequestContext"/> behind it is scoped.
    /// </para>
    /// <para>
    /// Satisfies <c>AddAuthorizationBehavior()</c>'s <c>Build()</c>-time check, so call it before the
    /// application behaviors' <c>Build()</c>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelRequestContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IRequestContextAccessor, RequestContextAccessor>();
        services.TryAddScoped<SecurityRequestContext>();
        services.AddTransient<IRequestContext>(static provider =>
            RequestContextScope.Current ?? provider.GetRequiredService<SecurityRequestContext>());

        return services;
    }
}
