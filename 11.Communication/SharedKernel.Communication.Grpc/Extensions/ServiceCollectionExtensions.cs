using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Communication.Grpc.Builders;
using SharedKernel.Communication.Grpc.Interceptors;

namespace SharedKernel.Communication.Grpc.Extensions;

/// <summary>
/// DI registration entry point for <c>SharedKernel.Communication.Grpc</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers platform-standard gRPC communication infrastructure:
    /// <see cref="IGrpcCommunicationBuilder"/>, <c>CorrelationTracingInterceptor</c>,
    /// and <c>TenantIdInterceptor</c> globally via <c>Grpc.Net.ClientFactory</c>.
    /// </summary>
    /// <returns>
    /// An <see cref="IGrpcCommunicationBuilder"/> for fluent typed-client registration via
    /// <c>AddGrpcClient&lt;TClient&gt;</c>.
    /// </returns>
    public static IGrpcCommunicationBuilder AddSharedKernelGrpcCommunication(
        this IServiceCollection services)
    {
        // IHttpContextAccessor required by TenantIdInterceptor for request-scope tenant resolution.
        // TryAdd avoids double-registration in multi-call scenarios.
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        // Interceptors registered as singletons — they hold no request-level state.
        // TenantIdInterceptor resolves ITenantProvider from request scope at call time via
        // IHttpContextAccessor, so singleton lifetime is correct.
        services.TryAddSingleton<CorrelationTracingInterceptor>();
        services.TryAddSingleton<TenantIdInterceptor>();

        return new GrpcCommunicationBuilder(services);
    }
}
