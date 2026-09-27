using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using CheckoutApi;
using InventoryApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CheckoutApi.Tests;

/// <summary>InventoryApi on two real loopback ports: REST (HTTP/1.1) and gRPC (HTTP/2).</summary>
public sealed class InventoryHost : WebApplicationFactory<InventoryStore>
{
    public const string ApiKey = "test-inventory-key";

    public InventoryHost()
    {
        HttpPort = FreePort();
        GrpcPort = FreePort();
        UseKestrel();
        StartServer();
    }

    public int HttpPort { get; }

    public int GrpcPort { get; }

    public InventoryStore Store => Services.GetRequiredService<InventoryStore>();

    /// <summary>A client of InventoryApi's REST side, with its API key.</summary>
    public HttpClient Rest()
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{HttpPort}") };
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Http:Url"] = $"http://127.0.0.1:{HttpPort}",
            ["Kestrel:Endpoints:Grpc:Url"] = $"http://127.0.0.1:{GrpcPort}",
            [ConfiguredApiKeyValidator.SettingName] = ApiKey,
        }));

    internal static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>CheckoutApi, in memory, calling InventoryApi at the ports in its Services section.</summary>
public sealed class CheckoutHost(
    int inventoryHttpPort,
    int inventoryGrpcPort,
    string apiKey = InventoryHost.ApiKey,
    IDictionary<string, string?>? settings = null)
    : WebApplicationFactory<PlaceOrderRequest>
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:inventory:http:0"] = $"http://127.0.0.1:{inventoryHttpPort}",
            ["Services:inventory:grpc:0"] = $"http://127.0.0.1:{inventoryGrpcPort}",
            ["SharedKernel:Communication:Clients:inventory:Authentication:ApiKey:Value"] = apiKey,
            ["SharedKernel:Communication:Clients:inventory-grpc:Authentication:ApiKey:Value"] = apiKey,
            // A dead service is found out quickly in a test.
            ["SharedKernel:Communication:Clients:inventory:Retry:BaseDelay"] = "00:00:00.050",
            ["SharedKernel:Communication:Clients:inventory-grpc:Retry:InitialBackoff"] = "00:00:00.050",
            ["SharedKernel:Communication:Clients:inventory-grpc:Retry:MaxBackoff"] = "00:00:00.100",
        }).AddInMemoryCollection(settings ?? new Dictionary<string, string?>()));

    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.TryGetProperty("errorCode", out JsonElement code) ? code.GetString() : null;
    }
}

[CollectionDefinition(Name)]
public sealed class ServicesCollection : ICollectionFixture<InventoryHost>
{
    public const string Name = "InventoryApi";
}
