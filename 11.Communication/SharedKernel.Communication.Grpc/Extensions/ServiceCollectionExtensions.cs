using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Grpc.Builders;

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
        // TODO: implement
        throw new NotImplementedException();
    }
}
