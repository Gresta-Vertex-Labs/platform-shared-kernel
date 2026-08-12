using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.Rest.Builders;
using SharedKernel.Communication.Rest.Handlers;
using SharedKernel.Communication.Rest.Options;

namespace SharedKernel.Communication.Rest.Extensions;

/// <summary>
/// DI registration entry point for <c>SharedKernel.Communication.Rest</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers platform-standard REST communication infrastructure: <see cref="IRestCommunicationBuilder"/>,
    /// <c>CorrelationIdDelegatingHandler</c> (transient), <c>TenantIdDelegatingHandler</c> (transient),
    /// <c>IdempotencyKeyDelegatingHandler</c> (transient — opt-in, only added to a client's pipeline when
    /// <c>RestClientOptions.EnableIdempotencyKeyPropagation</c> is <c>true</c>), and the STJ
    /// <c>ProblemDetailsJsonContext</c>.
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

        // All three delegation handlers registered as transient — they must never hold cross-request
        // state. TenantIdDelegatingHandler resolves ITenantProvider from request scope at call time via
        // IHttpContextAccessor, so it is safe to register as transient here. IdempotencyKeyDelegatingHandler
        // is registered unconditionally but only actually added to a given client's handler pipeline when
        // that client opts in via RestClientOptions.EnableIdempotencyKeyPropagation (P-364/WO-056).
        services.AddTransient<CorrelationIdDelegatingHandler>();
        services.AddTransient<TenantIdDelegatingHandler>();
        services.AddTransient<IdempotencyKeyDelegatingHandler>();

        // Register RestClientOptionsValidator so startup validation fires for invalid options.
        // Uses AddSingleton so the validator is registered exactly once (TryAdd would silently skip
        // on a second AddSharedKernelRestCommunication call, which is the desired idempotency).
        services.AddSingleton<IValidateOptions<RestClientOptions>, RestClientOptionsValidator>();

        return new RestCommunicationBuilder(services);
    }
}
