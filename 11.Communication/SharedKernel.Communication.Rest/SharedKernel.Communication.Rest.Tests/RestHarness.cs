using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Testing.Communication;

namespace SharedKernel.Communication.Rest.Tests;

/// <summary>The typed client every test registers: the interface the application injects and its implementation.</summary>
public interface IInventoryClient
{
    HttpClient Http { get; }
}

public sealed class InventoryClient(HttpClient http) : IInventoryClient
{
    public HttpClient Http { get; } = http;
}

public sealed record StockLevel(string Sku, int Available);

public sealed record Reservation(string Sku, int Quantity);

[JsonSerializable(typeof(StockLevel))]
[JsonSerializable(typeof(Reservation))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
public sealed partial class InventoryJson : JsonSerializerContext;

/// <summary>A service with one REST client, "inventory", whose requests reach <see cref="Stub"/>.</summary>
internal sealed class RestHarness : IDisposable
{
    public const string ClientName = "inventory";

    private readonly ServiceProvider _provider;

    public RestHarness(
        IDictionary<string, string?>? settings = null,
        Action<IRestClientBuilder>? configure = null,
        Action<IServiceCollection>? services = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"SharedKernel:Communication:Clients:{ClientName}:BaseAddress"] = "http://inventory",
                // No waiting between retries in tests.
                [$"SharedKernel:Communication:Clients:{ClientName}:Retry:BaseDelay"] = "00:00:00",
            })
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSharedKernelCommunication(configuration)
            .AddRestClient<IInventoryClient, InventoryClient>(ClientName, configure);
        collection.UseStubHttpMessageHandler(ClientName, Stub);
        services?.Invoke(collection);

        _provider = collection.BuildServiceProvider();
        _provider.GetRequiredService<IStartupValidator>().Validate();
    }

    public StubHttpMessageHandler Stub { get; } = new();

    public HttpClient Http => _provider.GetRequiredService<IInventoryClient>().Http;

    public IServiceProvider Services => _provider;

    public void Dispose() => _provider.Dispose();
}
