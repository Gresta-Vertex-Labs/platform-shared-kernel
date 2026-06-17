using GrpcCore = Grpc.Core;
using Grpc.Net.Client.Configuration;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Grpc.Interceptors;
using SharedKernel.Communication.Grpc.Options;
using SharedKernel.Communication.Internal.Resolvers;

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
        var options = new GrpcClientOptions { Address = address };
        configure?.Invoke(options);

        var hasAddress = !string.IsNullOrWhiteSpace(options.Address);
        var hasResolver = Services.Any(d => d.ServiceType == typeof(IServiceEndpointResolver));

        if (!hasAddress && !hasResolver)
        {
            throw new InvalidOperationException(
                $"GrpcClientOptions for '{typeof(TClient).Name}' requires either an Address or a registered " +
                $"IServiceEndpointResolver. Call AddK8sServiceDiscovery() or AddStaticServiceDiscovery() " +
                $"before registering typed gRPC clients without an Address.");
        }

        var clientName = typeof(TClient).Name;
        var enableRetry = options.EnableRetry;

        IHttpClientBuilder clientBuilder;

        if (hasAddress)
        {
            // Address is known at registration time — configure via GrpcClientFactoryOptions.
            clientBuilder = Services.AddGrpcClient<TClient>(o =>
            {
                o.Address = new Uri(options.Address!);
            });
        }
        else
        {
            // G-09: Address omitted — resolve via IServiceEndpointResolver at channel-creation time.
            // GrpcClientFactoryOptions.Address is configured using a factory action that resolves
            // the address synchronously from DI. ConfigureChannel sets the channel ServiceConfig.
            clientBuilder = Services.AddGrpcClient<TClient>((sp, o) =>
            {
                var resolver = sp.GetRequiredService<IServiceEndpointResolver>();
                // ResolveAsync never throws — safe to GetAwaiter().GetResult() here.
                // Called once per channel (channel is cached as singleton by ClientFactory).
                var uri = resolver.ResolveAsync(clientName, CancellationToken.None)
                    .GetAwaiter().GetResult();
                o.Address = uri;
            });
        }

        // Apply gRPC retry service config when enabled.
        if (enableRetry)
        {
            clientBuilder.ConfigureChannel(channelOptions =>
            {
                channelOptions.ServiceConfig = BuildRetryServiceConfig();
            });
        }

        // Register both interceptors globally for this client via Grpc.Net.ClientFactory.
        // Correlation tracing first (reads Activity.Current), then tenant ID.
        clientBuilder
            .AddInterceptor<CorrelationTracingInterceptor>(InterceptorScope.Channel)
            .AddInterceptor<TenantIdInterceptor>(InterceptorScope.Channel);

        return this;
    }

    private static ServiceConfig BuildRetryServiceConfig() =>
        new()
        {
            MethodConfigs =
            {
                new MethodConfig
                {
                    Names = { MethodName.Default },
                    RetryPolicy = new RetryPolicy
                    {
                        MaxAttempts = 3,
                        InitialBackoff = TimeSpan.FromMilliseconds(500),
                        MaxBackoff = TimeSpan.FromSeconds(5),
                        BackoffMultiplier = 1.5,
                        RetryableStatusCodes =
                        {
                            GrpcCore.StatusCode.Unavailable,
                            GrpcCore.StatusCode.DeadlineExceeded
                        }
                    }
                }
            }
        };
}
