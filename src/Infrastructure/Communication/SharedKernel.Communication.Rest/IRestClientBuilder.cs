using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Communication;

/// <summary>Adjusts one REST client inside <c>AddRestClient(name, client =&gt; …)</c>.</summary>
public interface IRestClientBuilder
{
    /// <summary>Gets the client's name: its <c>IHttpClientFactory</c> name and its configuration section.</summary>
    string Name { get; }

    /// <summary>
    /// Gets the underlying <see cref="IHttpClientBuilder"/>. A handler added to it runs once per call, after the
    /// platform's propagation handlers and outside the retry loop.
    /// </summary>
    IHttpClientBuilder HttpClientBuilder { get; }

    /// <summary>Changes the client's options after they are bound from configuration.</summary>
    /// <param name="configure">The change.</param>
    /// <returns>This builder.</returns>
    IRestClientBuilder Configure(Action<RestClientOptions> configure);

    /// <summary>
    /// Replaces the retry strategy with hedging (<see cref="RestClientOptions.Hedging"/>): a slow attempt is raced by a
    /// parallel one to the next endpoint. For latency-sensitive reads; POST and PATCH are hedged only with an
    /// <c>Idempotency-Key</c>.
    /// </summary>
    /// <returns>This builder.</returns>
    IRestClientBuilder UseHedging();

    /// <summary>
    /// Authenticates the client with tokens from <typeparamref name="TProvider"/>, registered as a singleton keyed by
    /// the client's name.
    /// </summary>
    /// <typeparam name="TProvider">The token source.</typeparam>
    /// <returns>This builder.</returns>
    IRestClientBuilder UseAccessTokenProvider<TProvider>()
        where TProvider : class, IAccessTokenProvider;
}
