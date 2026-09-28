using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;

namespace SharedKernel.Communication.Internal;

/// <summary>What the REST and gRPC clients both do to their <see cref="IHttpClientBuilder"/>.</summary>
internal static class ClientPipeline
{
    /// <summary>
    /// Binds <typeparamref name="TOptions"/> for the client from <c>SharedKernel:Communication:Clients:{name}</c>,
    /// validated at startup, after <paramref name="configure"/> changes applied in code.
    /// </summary>
    public static void BindOptions<TOptions>(ICommunicationBuilder builder, string name, Action<TOptions>? configure)
        where TOptions : CommunicationClientOptions
    {
        builder.Services.AddValidatedOptions<TOptions>(
            builder.Configuration.GetSection($"{CommunicationOptions.ClientsSectionName}:{name}"),
            name);

        if (configure is not null)
        {
            builder.Services.PostConfigure(name, configure);
        }
    }

    /// <summary>
    /// The connection handler: a <see cref="SocketsHttpHandler"/> with the client's TLS settings, rebuilt whenever
    /// <c>IHttpClientFactory</c> rotates handlers, so rotated certificate files are read again.
    /// </summary>
    public static void UseConnectionHandler<TOptions>(
        IHttpClientBuilder http,
        string name,
        Action<SocketsHttpHandler, TOptions>? configure = null)
        where TOptions : CommunicationClientOptions =>
        http.UseSocketsHttpHandler((handler, services) =>
        {
            TOptions options = services.GetRequiredService<IOptionsMonitor<TOptions>>().Get(name);
            ClientTls.Apply(handler, options.Tls);
            configure?.Invoke(handler, options);
        });

    /// <summary>
    /// The credential and the endpoint, per attempt: the authentication handler, then service discovery, innermost
    /// of the client's handlers.
    /// </summary>
    public static void AddCredentialsAndDiscovery<TOptions>(IHttpClientBuilder http, string name)
        where TOptions : CommunicationClientOptions
    {
        http.AddHttpMessageHandler(services => new ClientAuthenticationHandler(
            name,
            () => services.GetRequiredService<IOptionsMonitor<TOptions>>().Get(name).Authentication,
            services));

        // Microsoft.Extensions.ServiceDiscovery: resolves the host per request, round-robin across its endpoints.
        http.AddServiceDiscovery();
    }

    /// <summary>Makes <typeparamref name="TProvider"/> the client's token source.</summary>
    public static void UseAccessTokenProvider<TOptions, TProvider>(IServiceCollection services, string name)
        where TOptions : CommunicationClientOptions
        where TProvider : class, IAccessTokenProvider
    {
        services.TryAddKeyedSingleton<IAccessTokenProvider, TProvider>(name);
        services.PostConfigure<TOptions>(name, o => o.Authentication.Mode = ClientAuthenticationMode.AccessTokenProvider);
    }
}
