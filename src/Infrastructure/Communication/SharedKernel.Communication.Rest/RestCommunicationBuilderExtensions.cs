using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.Internal;
using SharedKernel.Communication.Rest.Internal;
using SharedKernel.Execution.Context;

namespace SharedKernel.Communication;

/// <summary>Adds typed REST clients to SharedKernel communication.</summary>
public static class RestCommunicationBuilderExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TClient"/>, implemented by <typeparamref name="TImplementation"/>, as a typed
    /// client whose <see cref="HttpClient"/> is configured from <c>SharedKernel:Communication:Clients:{name}</c>.
    /// </summary>
    /// <typeparam name="TClient">The interface the application injects.</typeparam>
    /// <typeparam name="TImplementation">The class that takes the <see cref="HttpClient"/> in its constructor.</typeparam>
    /// <param name="builder">The builder from <c>AddSharedKernelCommunication</c>.</param>
    /// <param name="name">The client's name, unique among REST and gRPC clients.</param>
    /// <param name="configure">Adjusts the client: options in code, hedging, a token provider, extra handlers.</param>
    /// <returns>The builder, to add more clients.</returns>
    /// <remarks>
    /// Each call runs, outermost first: the caller's correlation id, tenant, actor and client headers; the
    /// <c>Idempotency-Key</c> (when enabled); the handlers added through <see cref="IRestClientBuilder.HttpClientBuilder"/>;
    /// then, for each attempt, the resilience pipeline (timeouts, retries or hedging, circuit breaker), the credential,
    /// and service discovery picking the endpoint.
    /// </remarks>
    public static ICommunicationBuilder AddRestClient<TClient, TImplementation>(
        this ICommunicationBuilder builder,
        string name,
        Action<IRestClientBuilder>? configure = null)
        where TClient : class
        where TImplementation : class, TClient
    {
        ArgumentNullException.ThrowIfNull(builder);
        CommunicationClientRegistry.Reserve(builder.Services, name, "REST");

        return Register(builder, name, builder.Services.AddHttpClient<TClient, TImplementation>(name), configure);
    }

    /// <summary>
    /// Registers the class <typeparamref name="TClient"/> as a typed client whose <see cref="HttpClient"/> is
    /// configured from <c>SharedKernel:Communication:Clients:{name}</c>.
    /// </summary>
    /// <typeparam name="TClient">The class that takes the <see cref="HttpClient"/> in its constructor.</typeparam>
    /// <param name="builder">The builder from <c>AddSharedKernelCommunication</c>.</param>
    /// <param name="name">The client's name, unique among REST and gRPC clients.</param>
    /// <param name="configure">Adjusts the client: options in code, hedging, a token provider, extra handlers.</param>
    /// <returns>The builder, to add more clients.</returns>
    /// <exception cref="ArgumentException"><typeparamref name="TClient"/> is an interface or abstract.</exception>
    public static ICommunicationBuilder AddRestClient<TClient>(
        this ICommunicationBuilder builder,
        string name,
        Action<IRestClientBuilder>? configure = null)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (typeof(TClient).IsInterface || typeof(TClient).IsAbstract)
        {
            throw new ArgumentException(
                $"{typeof(TClient).Name} cannot be created; register it with AddRestClient<{typeof(TClient).Name}, TImplementation>(\"{name}\").",
                nameof(TClient));
        }

        CommunicationClientRegistry.Reserve(builder.Services, name, "REST");
        return Register(builder, name, builder.Services.AddHttpClient<TClient>(name), configure);
    }

    private static ICommunicationBuilder Register(
        ICommunicationBuilder builder,
        string name,
        IHttpClientBuilder http,
        Action<IRestClientBuilder>? configure)
    {
        ClientPipeline.BindOptions<RestClientOptions>(builder, name, configure: null);

        http.ConfigureHttpClient((services, client) =>
        {
            RestClientOptions options = Options(services, name);
            client.BaseAddress = options.BaseAddress;

            // The resilience pipeline owns every timeout; HttpClient's own 100 s would cut a longer call short.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        ClientPipeline.UseConnectionHandler<RestClientOptions>(http, name);

        // Once per call, outside the retry loop, so every attempt carries the same values.
        http.AddHttpMessageHandler(services => new RequestContextPropagationHandler(services.GetRequiredService<IRequestContextAccessor>()));
        http.AddHttpMessageHandler(services => new IdempotencyKeyHandler(() => Options(services, name).PropagateIdempotencyKey));

        var client = new RestClientBuilder(name, http, builder.Services);
        configure?.Invoke(client);

        if (client.Hedging)
        {
            RestResilience.AddHedging(http, name);
        }
        else
        {
            RestResilience.AddStandard(http, name);
        }

        ClientPipeline.AddCredentialsAndDiscovery<RestClientOptions>(http, name);
        return builder;
    }

    internal static RestClientOptions Options(IServiceProvider services, string name) =>
        services.GetRequiredService<IOptionsMonitor<RestClientOptions>>().Get(name);

    private sealed class RestClientBuilder(string name, IHttpClientBuilder http, IServiceCollection services) : IRestClientBuilder
    {
        public string Name { get; } = name;

        public IHttpClientBuilder HttpClientBuilder { get; } = http;

        public bool Hedging { get; private set; }

        public IRestClientBuilder Configure(Action<RestClientOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            services.PostConfigure(Name, configure);
            return this;
        }

        public IRestClientBuilder UseHedging()
        {
            Hedging = true;
            return this;
        }

        public IRestClientBuilder UseAccessTokenProvider<TProvider>()
            where TProvider : class, IAccessTokenProvider
        {
            ClientPipeline.UseAccessTokenProvider<RestClientOptions, TProvider>(services, Name);
            return this;
        }
    }
}
