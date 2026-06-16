using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Grpc.Options;

namespace SharedKernel.Communication.Grpc.Builders;

/// <summary>Default implementation of <see cref="IGrpcCommunicationBuilder"/>.</summary>
internal sealed class GrpcCommunicationBuilder(IServiceCollection services) : IGrpcCommunicationBuilder
{
    /// <inheritdoc />
    public IServiceCollection Services { get; } = services;

    /// <inheritdoc />
    public IGrpcCommunicationBuilder AddGrpcClient<TClient>(
        string address,
        Action<GrpcClientOptions>? configure = null)
        where TClient : class
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
