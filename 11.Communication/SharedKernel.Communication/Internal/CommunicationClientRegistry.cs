using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Communication.Internal;

/// <summary>
/// The names of the registered clients. REST and gRPC clients share one <c>IHttpClientFactory</c> namespace and one
/// configuration section, so a name may be used once.
/// </summary>
internal sealed class CommunicationClientRegistry
{
    private readonly Dictionary<string, string> _clients = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Names => _clients.Keys;

    /// <summary>Finds the registry <c>AddSharedKernelCommunication</c> put in <paramref name="services"/>.</summary>
    public static CommunicationClientRegistry? Find(IServiceCollection services) =>
        services.LastOrDefault(d => d.ServiceType == typeof(CommunicationClientRegistry))?.ImplementationInstance as CommunicationClientRegistry;

    /// <summary>Records a client name, failing when it is already taken.</summary>
    public static void Reserve(IServiceCollection services, string name, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var registry = Find(services)
            ?? throw new InvalidOperationException(
                $"Call services.AddSharedKernelCommunication(configuration) before adding the {kind} client '{name}'.");

        if (name.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException($"Client name '{name}' must not contain ':' (it names a configuration section).", nameof(name));
        }

        if (!registry._clients.TryAdd(name, kind))
        {
            throw new InvalidOperationException(
                $"A {registry._clients[name]} client named '{name}' is already registered; client names must be unique.");
        }
    }
}
