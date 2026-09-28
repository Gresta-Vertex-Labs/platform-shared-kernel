using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.Grpc.Internal;
using SharedKernel.Communication.Internal;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Communication;

/// <summary>Adds typed gRPC clients to SharedKernel communication.</summary>
public static class GrpcCommunicationBuilderExtensions
{
    /// <summary>
    /// Registers the generated client <typeparamref name="TClient"/> through <c>Grpc.Net.ClientFactory</c>, configured
    /// from <c>SharedKernel:Communication:Clients:{name}</c>.
    /// </summary>
    /// <typeparam name="TClient">The generated client class (<c>Inventory.InventoryClient</c>).</typeparam>
    /// <param name="builder">The builder from <c>AddSharedKernelCommunication</c>.</param>
    /// <param name="name">The client's name, unique among REST and gRPC clients.</param>
    /// <param name="configure">Adjusts the client: options in code, a token provider, extra handlers.</param>
    /// <returns>The builder, to add more clients.</returns>
    /// <remarks>
    /// Every call gets the caller's correlation id, tenant, actor and client as metadata (once per call, before the
    /// retries) and the configured deadline unless it sets its own. Each attempt then gets the credential, and service
    /// discovery picks the endpoint: with <see cref="ServiceDiscoveryMode.Dns"/> over a headless service, calls are
    /// spread across the pods instead of staying on the one a long-lived HTTP/2 connection reached.
    /// </remarks>
    public static ICommunicationBuilder AddGrpcClient<TClient>(
        this ICommunicationBuilder builder,
        string name,
        Action<IGrpcClientBuilder>? configure = null)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        CommunicationClientRegistry.Reserve(builder.Services, name, "gRPC");
        ClientPipeline.BindOptions<GrpcClientOptions>(builder, name, configure: null);
        builder.Services.TryAddSingleton<RequestContextInterceptor>();

        IHttpClientBuilder http = builder.Services.AddGrpcClient<TClient>(name, (services, factory) =>
        {
            GrpcClientOptions options = Options(services, name);
            factory.Address = options.Address;

            // The deadline instant is computed when the call starts; a call's own deadline wins.
            IClock clock = services.GetRequiredService<IClock>();
            TimeSpan deadline = options.Deadline;
            factory.CallOptionsActions.Add(context =>
            {
                if (context.CallOptions.Deadline is null)
                {
                    context.CallOptions = context.CallOptions.WithDeadline(clock.UtcNow.UtcDateTime + deadline);
                }
            });
        });

        http.ConfigureChannel((services, channel) =>
        {
            GrpcClientOptions options = Options(services, name);
            channel.ServiceConfig = RetryServiceConfig(options.Retry);
            channel.MaxReceiveMessageSize = options.MaxReceiveMessageSize ?? channel.MaxReceiveMessageSize;
        });

        // Once per call, before the client's own retries.
        http.AddInterceptor<RequestContextInterceptor>(InterceptorScope.Channel);

        ClientPipeline.UseConnectionHandler<GrpcClientOptions>(http, name, static (handler, options) =>
        {
            // Several connections once one is full of streams; pings find a dead connection before a call waits on it.
            handler.EnableMultipleHttp2Connections = true;
            handler.KeepAlivePingDelay = options.KeepAlive.PingDelay;
            handler.KeepAlivePingTimeout = options.KeepAlive.PingTimeout;
            handler.KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests;
        });

        configure?.Invoke(new GrpcClientBuilder(name, http, builder.Services));

        ClientPipeline.AddCredentialsAndDiscovery<GrpcClientOptions>(http, name);
        return builder;
    }

    private static GrpcClientOptions Options(IServiceProvider services, string name) =>
        services.GetRequiredService<IOptionsMonitor<GrpcClientOptions>>().Get(name);

    private static ServiceConfig? RetryServiceConfig(GrpcRetryOptions retry)
    {
        if (retry.MaxAttempts <= 1)
        {
            return null;
        }

        var policy = new RetryPolicy
        {
            MaxAttempts = retry.MaxAttempts,
            InitialBackoff = retry.InitialBackoff,
            MaxBackoff = retry.MaxBackoff,
            BackoffMultiplier = retry.BackoffMultiplier,
        };

        foreach (StatusCode code in retry.RetryableStatusCodes ?? [StatusCode.Unavailable])
        {
            policy.RetryableStatusCodes.Add(code);
        }

        return new ServiceConfig { MethodConfigs = { new MethodConfig { Names = { MethodName.Default }, RetryPolicy = policy } } };
    }

    private sealed class GrpcClientBuilder(string name, IHttpClientBuilder http, IServiceCollection services) : IGrpcClientBuilder
    {
        public string Name { get; } = name;

        public IHttpClientBuilder HttpClientBuilder { get; } = http;

        public IGrpcClientBuilder Configure(Action<GrpcClientOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            services.PostConfigure(Name, configure);
            return this;
        }

        public IGrpcClientBuilder UseAccessTokenProvider<TProvider>()
            where TProvider : class, IAccessTokenProvider
        {
            ClientPipeline.UseAccessTokenProvider<GrpcClientOptions, TProvider>(services, Name);
            return this;
        }
    }
}
