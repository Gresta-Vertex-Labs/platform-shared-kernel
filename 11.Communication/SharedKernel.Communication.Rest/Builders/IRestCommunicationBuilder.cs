using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Rest.Options;

namespace SharedKernel.Communication.Rest.Builders;

/// <summary>
/// Fluent builder for registering typed REST clients with platform-standard resilience,
/// correlation-ID propagation, and tenant-ID propagation.
/// </summary>
public interface IRestCommunicationBuilder
{
    /// <summary>Gets the underlying service collection for further registration.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers a typed REST client with StandardResilienceHandler and RequestContextDelegatingHandler
    /// (correlation id, tenant, actor and client headers) already wired in the correct pipeline order.
    /// </summary>
    /// <typeparam name="TClient">The typed client interface or class.</typeparam>
    /// <param name="name">Logical name used for service-discovery resolution when BaseAddress is omitted.</param>
    /// <param name="configure">Optional delegate to customise <see cref="RestClientOptions"/> for this client.</param>
    /// <returns>This builder for fluent chaining.</returns>
    IRestCommunicationBuilder AddRestClient<TClient>(
        string name,
        Action<RestClientOptions>? configure = null)
        where TClient : class;
}
