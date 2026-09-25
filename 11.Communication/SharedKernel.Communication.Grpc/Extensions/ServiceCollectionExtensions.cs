using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.Grpc.Builders;
using SharedKernel.Communication.Grpc.Interceptors;
using SharedKernel.Communication.Grpc.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Clocks;

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
        // Both interceptors read the ambient caller through IRequestContextAccessor at call time (P-566) —
        // never IHttpContextAccessor — so they hold no request-level state and are singletons.
        services.TryAddSingleton<IRequestContextAccessor, RequestContextAccessor>();
        services.TryAddSingleton<CorrelationTracingInterceptor>();
        services.TryAddSingleton<TenantIdInterceptor>();

        // P-359/WO-056: safety-net IClock default so AddGrpcClient<TClient>'s real per-call deadline
        // enforcement keeps working with zero new caller-side setup — TryAdd so a consuming service's
        // own IClock registration (of any implementation) always wins. Mirrors AddK8sServiceDiscovery's
        // identical safety-net registration added for the same reason under P-357 (.Internal).
        services.TryAddSingleton<IClock, SystemClock>();

        // Registered for any future direct IOptions<GrpcClientOptions> consumer — not the enforcement
        // mechanism relied upon, since GrpcClientOptions is validated directly inside AddGrpcClient<TClient>
        // (P-359/WO-056), mirroring RestClientOptionsValidator's identical registration rationale.
        services.AddSingleton<IValidateOptions<GrpcClientOptions>, GrpcClientOptionsValidator>();

        return new GrpcCommunicationBuilder(services);
    }
}
