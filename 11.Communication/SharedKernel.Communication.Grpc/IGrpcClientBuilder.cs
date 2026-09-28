using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Communication;

/// <summary>Adjusts one gRPC client inside <c>AddGrpcClient(name, client =&gt; …)</c>.</summary>
public interface IGrpcClientBuilder
{
    /// <summary>Gets the client's name: its <c>IHttpClientFactory</c> name and its configuration section.</summary>
    string Name { get; }

    /// <summary>
    /// Gets the underlying <see cref="IHttpClientBuilder"/> of <c>Grpc.Net.ClientFactory</c>. A handler added to it runs
    /// for each attempt, before the credential and service discovery.
    /// </summary>
    IHttpClientBuilder HttpClientBuilder { get; }

    /// <summary>Changes the client's options after they are bound from configuration.</summary>
    /// <param name="configure">The change.</param>
    /// <returns>This builder.</returns>
    IGrpcClientBuilder Configure(Action<GrpcClientOptions> configure);

    /// <summary>
    /// Authenticates the client with tokens from <typeparamref name="TProvider"/>, registered as a singleton keyed by
    /// the client's name.
    /// </summary>
    /// <typeparam name="TProvider">The token source.</typeparam>
    /// <returns>This builder.</returns>
    IGrpcClientBuilder UseAccessTokenProvider<TProvider>()
        where TProvider : class, IAccessTokenProvider;
}
