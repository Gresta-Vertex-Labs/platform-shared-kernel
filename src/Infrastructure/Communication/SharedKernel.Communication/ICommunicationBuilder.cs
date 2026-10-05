using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Communication;

/// <summary>
/// The builder <see cref="CommunicationServiceCollectionExtensions.AddSharedKernelCommunication"/> returns. Add clients
/// to it with <c>AddRestClient</c> (<c>SharedKernel.Communication.Rest</c>) and <c>AddGrpcClient</c>
/// (<c>SharedKernel.Communication.Grpc</c>).
/// </summary>
public interface ICommunicationBuilder
{
    /// <summary>Gets the service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>Gets the configuration each client binds its settings from.</summary>
    IConfiguration Configuration { get; }
}
