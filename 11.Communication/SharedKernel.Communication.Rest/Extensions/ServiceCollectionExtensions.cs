using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.Rest.Builders;
using SharedKernel.Communication.Rest.Handlers;
using SharedKernel.Communication.Rest.Options;
using SharedKernel.Execution.Context;

namespace SharedKernel.Communication.Rest.Extensions;

/// <summary>
/// DI registration entry point for <c>SharedKernel.Communication.Rest</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers platform-standard REST communication infrastructure: <see cref="IRestCommunicationBuilder"/>,
    /// <see cref="IRequestContextAccessor"/> (unless one is registered), <c>RequestContextDelegatingHandler</c>
    /// (transient — writes the ambient caller's correlation id, tenant, actor and client headers),
    /// <c>IdempotencyKeyDelegatingHandler</c> (transient — opt-in, only added to a client's pipeline when
    /// <c>RestClientOptions.EnableIdempotencyKeyPropagation</c> is <c>true</c>), and the STJ
    /// <c>ProblemDetailsJsonContext</c>.
    /// </summary>
    /// <returns>
    /// An <see cref="IRestCommunicationBuilder"/> for fluent typed-client registration via
    /// <c>AddRestClient&lt;TClient&gt;</c>.
    /// </returns>
    /// <remarks>
    /// The caller comes from <see cref="IRequestContextAccessor"/>, the ambient context every inbound adapter opens —
    /// not from the inbound <c>HttpContext</c> — so this package has no ASP.NET Core dependency and a call made from a
    /// message consumer, workflow activity or scheduled job propagates exactly like one made from an HTTP request.
    /// </remarks>
    public static IRestCommunicationBuilder AddSharedKernelRestCommunication(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IRequestContextAccessor, RequestContextAccessor>();

        // Both delegation handlers registered as transient — they must never hold cross-request state.
        // IdempotencyKeyDelegatingHandler is registered unconditionally but only actually added to a given
        // client's handler pipeline when that client opts in via RestClientOptions.EnableIdempotencyKeyPropagation
        // (P-364/WO-056).
        services.AddTransient<RequestContextDelegatingHandler>();
        services.AddTransient<IdempotencyKeyDelegatingHandler>();

        // Register RestClientOptionsValidator so startup validation fires for invalid options.
        // Uses AddSingleton so the validator is registered exactly once (TryAdd would silently skip
        // on a second AddSharedKernelRestCommunication call, which is the desired idempotency).
        services.AddSingleton<IValidateOptions<RestClientOptions>, RestClientOptionsValidator>();

        return new RestCommunicationBuilder(services);
    }
}
