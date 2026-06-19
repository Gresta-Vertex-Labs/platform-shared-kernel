using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Grpc.Options;

namespace SharedKernel.Communication.Grpc.Builders;

/// <summary>
/// Fluent builder for registering typed gRPC clients with platform-standard OTel tracing,
/// correlation-ID propagation, and tenant-ID propagation interceptors.
/// </summary>
public interface IGrpcCommunicationBuilder
{
    /// <summary>Gets the underlying service collection for further registration.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers a typed gRPC client with <c>CorrelationTracingInterceptor</c> and
    /// <c>TenantIdInterceptor</c> wired globally via <c>Grpc.Net.ClientFactory</c> channel caching.
    /// </summary>
    /// <typeparam name="TClient">The generated gRPC client class.</typeparam>
    /// <param name="address">
    /// Channel address. May be omitted (<see langword="null"/>) when <c>IServiceEndpointResolver</c>
    /// is registered in DI — the resolver will supply the address at channel-creation time.
    /// Throws <see cref="InvalidOperationException"/> when both <paramref name="address"/> is
    /// <see langword="null"/> or whitespace and no <c>IServiceEndpointResolver</c> is present.
    /// </param>
    /// <param name="configure">Optional delegate to customise <see cref="GrpcClientOptions"/>.</param>
    /// <returns>This builder for fluent chaining.</returns>
    IGrpcCommunicationBuilder AddGrpcClient<TClient>(
        string? address = null,
        Action<GrpcClientOptions>? configure = null)
        where TClient : class;
}
