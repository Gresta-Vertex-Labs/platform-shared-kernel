using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Rest.Options;

namespace SharedKernel.Communication.Rest.Builders;

/// <summary>
/// Default implementation of <see cref="IRestCommunicationBuilder"/>.
/// </summary>
internal sealed class RestCommunicationBuilder(IServiceCollection services) : IRestCommunicationBuilder
{
    /// <inheritdoc />
    public IServiceCollection Services { get; } = services;

    /// <inheritdoc />
    public IRestCommunicationBuilder AddRestClient<TClient>(
        string name,
        Action<RestClientOptions>? configure = null)
        where TClient : class
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}
