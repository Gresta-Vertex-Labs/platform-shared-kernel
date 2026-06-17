using SharedKernel.Communication.Internal.Resolvers;

namespace SharedKernel.Communication.Rest.Handlers;

/// <summary>
/// Resolves the base URI for a typed HTTP client via <see cref="IServiceEndpointResolver"/> at request time.
/// Prepends the resolved URI to the outgoing <see cref="HttpRequestMessage.RequestUri"/>.
/// Used when <c>RestClientOptions.BaseAddress</c> is omitted and <c>IServiceEndpointResolver</c> is
/// registered in the DI container. Registered as transient per typed-client name.
/// </summary>
internal sealed class ServiceDiscoveryResolvingHandler(
    IServiceEndpointResolver resolver,
    string serviceName) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var baseUri = await resolver.ResolveAsync(serviceName, cancellationToken).ConfigureAwait(false);

        // Rewrite relative URIs only; absolute URIs are already fully-qualified by the caller
        if (request.RequestUri is { IsAbsoluteUri: false } relativeUri)
        {
            request.RequestUri = new Uri(baseUri, relativeUri);
        }
        else if (request.RequestUri is null)
        {
            request.RequestUri = baseUri;
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
