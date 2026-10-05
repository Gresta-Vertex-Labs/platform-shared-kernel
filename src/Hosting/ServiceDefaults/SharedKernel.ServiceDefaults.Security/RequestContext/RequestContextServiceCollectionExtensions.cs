using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>Registers the <c>12.Security</c>-backed <see cref="IRequestContext"/>.</summary>
public static class RequestContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IRequestContext"/>: the ambient <see cref="RequestContextScope.Current"/> when an inbound
    /// adapter opened one, otherwise the scope's <see cref="SecurityRequestContext"/> over <see cref="IUserContext"/>.
    /// Also registers <see cref="IRequestContextAccessor"/> unless one is already registered, and refuses the W3C
    /// baggage of incoming HTTP requests (see <see cref="RequestContextOptions.TrustInboundBaggage"/>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Adjusts <see cref="RequestContextOptions"/>; may be <see langword="null"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Registered with <c>Add</c>, not <c>TryAdd</c>, so it replaces the fail-closed
    /// <see cref="AnonymousRequestContext"/> default that <c>SharedKernel.Persistence.EfCore</c>'s
    /// <c>Build()</c> registers, whichever of the two calls comes first. Requires an
    /// <see cref="IUserContext"/> registration (e.g. from <c>AddOidcAuthentication(...)</c>).
    /// </para>
    /// <para>
    /// The ambient context wins because the inbound adapters own it: in an HTTP request (REST, gRPC and SignalR alike,
    /// since they share the pipeline) it is the scope
    /// <see cref="RequestContextApplicationBuilderExtensions.UseSharedKernelRequestContext"/> opens (the caller plus
    /// the request's correlation id), refined by <c>SharedKernel.MultiTenancy</c>'s <c>TenantResolutionMiddleware</c>
    /// when that is used; on a SignalR hub invocation it is the connection's scope, reopened by
    /// <c>SharedKernel.Presentation.SignalR</c>'s hub filter; in a message consumer, workflow activity or scheduled job
    /// it is the scope that adapter opens. <see cref="IRequestContext"/> is therefore registered as transient; the
    /// <see cref="SecurityRequestContext"/> behind it is scoped.
    /// </para>
    /// <para>
    /// <b>Inbound baggage.</b> The <see cref="DistributedContextPropagator"/> ASP.NET Core hosting reads requests with
    /// is decorated to take no baggage from the caller, so not even hosting's own first log record carries it, and the
    /// middleware removes any inbound item still on the request's activity (P-579; previously
    /// <c>SharedKernel.Presentation.WebApi</c>'s <c>TrustInboundBaggage</c>). Call this method before
    /// <c>builder.Build()</c> so the decoration reaches the host.
    /// </para>
    /// <para>
    /// Safe to call more than once; each <paramref name="configure"/> is applied.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelRequestContext(
        this IServiceCollection services,
        Action<RequestContextOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<RequestContextOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(RequestContextMarker)))
        {
            return services;
        }

        services.AddSingleton<RequestContextMarker>();
        services.TryAddSingleton<IRequestContextAccessor, RequestContextAccessor>();
        services.TryAddScoped<SecurityRequestContext>();
        services.AddTransient<IRequestContext>(static provider =>
            RequestContextScope.Current ?? provider.GetRequiredService<SecurityRequestContext>());
        RefuseInboundBaggage(services);

        return services;
    }

    // Hosting reads each request's trace context and baggage with the DI propagator before any middleware runs. The
    // decorator refuses the caller's baggage there (unless TrustInboundBaggage), so not even hosting's own first log
    // record carries it; the middleware removes whatever reaches the activity another way.
    private static void RefuseInboundBaggage(IServiceCollection services)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var original = services[index];
            if (original.ServiceType != typeof(DistributedContextPropagator) || original.IsKeyedService)
            {
                continue;
            }

            services[index] = ServiceDescriptor.Describe(
                typeof(DistributedContextPropagator),
                provider => new InboundBaggagePropagator(
                    (DistributedContextPropagator)CreateInner(provider, original),
                    provider.GetRequiredService<IOptions<RequestContextOptions>>()),
                original.Lifetime);
            return;
        }

        // Not yet registered: the web host adds its own with TryAdd, and keeps this one.
        services.AddSingleton<DistributedContextPropagator>(static provider => new InboundBaggagePropagator(
            DistributedContextPropagator.Current,
            provider.GetRequiredService<IOptions<RequestContextOptions>>()));
    }

    private static object CreateInner(IServiceProvider provider, ServiceDescriptor descriptor) =>
        descriptor.ImplementationInstance
        ?? descriptor.ImplementationFactory?.Invoke(provider)
        ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);

    private sealed class RequestContextMarker;
}
