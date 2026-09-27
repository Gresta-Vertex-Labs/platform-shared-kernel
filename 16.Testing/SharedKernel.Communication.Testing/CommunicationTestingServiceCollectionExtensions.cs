using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Testing.Communication;

/// <summary>Puts test doubles under the clients a service registered with <c>AddRestClient</c>.</summary>
public static class CommunicationTestingServiceCollectionExtensions
{
    /// <summary>
    /// Makes <paramref name="stub"/> the connection of the client named <paramref name="clientName"/>: every handler of
    /// the client still runs, and the requests reach the stub instead of the network. Call it after the client is
    /// registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientName">The name the client was registered under.</param>
    /// <param name="stub">The stub.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection UseStubHttpMessageHandler(
        this IServiceCollection services,
        string clientName,
        StubHttpMessageHandler stub)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentNullException.ThrowIfNull(stub);

        // The factory disposes the handlers it rotates; the stub outlives them, so each rotation gets a wrapper.
        services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => new NonDisposingHandler(stub));
        return services;
    }

    private sealed class NonDisposingHandler(StubHttpMessageHandler stub) : DelegatingHandler(stub)
    {
        protected override void Dispose(bool disposing)
        {
            // Leaves the shared stub alive; the base would dispose it.
        }
    }
}
