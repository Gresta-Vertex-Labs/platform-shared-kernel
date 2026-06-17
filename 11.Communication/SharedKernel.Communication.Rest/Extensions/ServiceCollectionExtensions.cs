using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Communication.Rest.Builders;
using SharedKernel.Communication.Rest.Handlers;

namespace SharedKernel.Communication.Rest.Extensions;

/// <summary>
/// DI registration entry point for <c>SharedKernel.Communication.Rest</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers platform-standard REST communication infrastructure: <see cref="IRestCommunicationBuilder"/>,
    /// <c>CorrelationIdDelegatingHandler</c> (transient), <c>TenantIdDelegatingHandler</c> (transient),
    /// and the STJ <c>ProblemDetailsJsonContext</c>.
    /// </summary>
    /// <returns>
    /// An <see cref="IRestCommunicationBuilder"/> for fluent typed-client registration via
    /// <c>AddRestClient&lt;TClient&gt;</c>.
    /// </returns>
    public static IRestCommunicationBuilder AddSharedKernelRestCommunication(
        this IServiceCollection services)
    {
        // IHttpContextAccessor is required by TenantIdDelegatingHandler to resolve ITenantProvider
        // from the current request scope. TryAdd avoids double-registration in multi-call scenarios.
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        // Both delegation handlers registered as transient — they must never hold cross-request state.
        // TenantIdDelegatingHandler resolves ITenantProvider from request scope at call time via
        // IHttpContextAccessor, so it is safe to register as transient here.
        services.AddTransient<CorrelationIdDelegatingHandler>();
        services.AddTransient<TenantIdDelegatingHandler>();

        return new RestCommunicationBuilder(services);
    }
}
