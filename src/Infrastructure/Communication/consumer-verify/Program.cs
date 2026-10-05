// consumer-verify — composes the 11.Communication packages exactly as a service does: settings from configuration,
// one AddSharedKernelCommunication chain, a real generic host. Every call goes through the packages' own pipelines;
// the only fake is the connection handler that stands in for the network.
//
//   1. A REST client (interface + implementation) and a gRPC client register from configuration and start cleanly.
//   2. Invalid settings fail IHost.StartAsync, naming the setting.
//   3. The Services section resolves a client's host (service discovery), and the caller's correlation id travels.
//   4. A platform ProblemDetails reads back as the service's Error; an unreachable service is an Error, not an exception.
//   5. A gRPC rich status reads back as the service's Error; google.type.Money converts both ways.

using System.Net;
using System.Text;
using Google.Rpc;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Communication;
using SharedKernel.Domain.Monetary;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using RpcStatus = Google.Rpc.Status;

await Surface1_ClientsRegisterFromConfiguration();
await Surface2_InvalidSettingsFailAtStartup();
await Surface3_DiscoveryAndPropagation();
await Surface4_ResultsNotExceptions();
await Surface5_GrpcErrorsAndMoney();

Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");
return;

static async Task Surface1_ClientsRegisterFromConfiguration()
{
    using IHost host = BuildHost(new()
    {
        ["SharedKernel:Communication:Clients:inventory:BaseAddress"] = "http://inventory",
        ["SharedKernel:Communication:Clients:pricing:Address"] = "http://pricing",
    });
    await host.StartAsync();

    Verify(host.Services.GetRequiredService<IInventoryClient>() is InventoryClient, "IInventoryClient resolves to its implementation");
    Verify(host.Services.GetRequiredService<SampleGrpcClient>() is not null, "the gRPC client resolves");

    await host.StopAsync();
    Console.WriteLine("Surface 1 PASS: REST and gRPC clients register from SharedKernel:Communication:Clients and start.");
}

static async Task Surface2_InvalidSettingsFailAtStartup()
{
    using IHost host = BuildHost(new()
    {
        ["SharedKernel:Communication:Clients:inventory:AttemptTimeout"] = "00:00:05",
        ["SharedKernel:Communication:Clients:pricing:Address"] = "https+http://pricing",
    });

    // Both clients are invalid, so the host reports both at once.
    Exception? failure = null;
    try
    {
        await host.StartAsync();
    }
    catch (Exception exception)
    {
        failure = exception;
    }

    // A client's resilience pipeline reads its options too, so one invalid client may be reported twice.
    var messages = (failure as AggregateException)?.InnerExceptions.OfType<OptionsValidationException>().Select(e => e.Message).Distinct().ToList() ?? [];
    Verify(messages.Count == 2, "StartAsync fails on both invalid clients");
    Verify(messages.Any(m => m.Contains("BaseAddress is required", StringComparison.Ordinal)), "the missing BaseAddress is named");
    Verify(messages.Any(m => m.Contains("must be an http or https address", StringComparison.Ordinal)), "the gRPC address a channel cannot use is named");
    Console.WriteLine("Surface 2 PASS: a client without an address, or with one it cannot use, stops the host from starting.");
}

static async Task Surface3_DiscoveryAndPropagation()
{
    var connection = new RecordingConnection(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"sku":"sku-1","available":3}""", Encoding.UTF8, "application/json"),
    });
    using IHost host = BuildHost(
        new()
        {
            ["SharedKernel:Communication:Clients:inventory:BaseAddress"] = "http://inventory",
            ["SharedKernel:Communication:Clients:pricing:Address"] = "http://pricing",
            ["Services:inventory:http:0"] = "http://10.1.2.3:8080",
        },
        connection);
    await host.StartAsync();

    Result<StockLevel> stock = await host.Services.GetRequiredService<IInventoryClient>().GetStockAsync("sku-1", CancellationToken.None);

    Verify(stock.IsSuccess && stock.Value.Available == 3, "the typed client reads the body as a Result");
    Verify(connection.LastUri!.Authority == "10.1.2.3:8080", "http://inventory resolved to the endpoint in Services:inventory");
    Verify(connection.Last!.Headers.Contains("X-Correlation-Id"), "the call carries a correlation id");
    await host.StopAsync();
    Console.WriteLine("Surface 3 PASS: service discovery and caller propagation run on every call.");
}

static async Task Surface4_ResultsNotExceptions()
{
    var connection = new RecordingConnection(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
    {
        Content = new StringContent("""{"status":404,"detail":"No such SKU.","errorCode":"inventory.sku_not_found"}""", Encoding.UTF8, "application/problem+json"),
    });
    using IHost host = BuildHost(
        new()
        {
            ["SharedKernel:Communication:Clients:inventory:BaseAddress"] = "http://inventory",
            ["SharedKernel:Communication:Clients:pricing:Address"] = "http://pricing",
        },
        connection);
    await host.StartAsync();
    IInventoryClient client = host.Services.GetRequiredService<IInventoryClient>();

    Result<StockLevel> missing = await client.GetStockAsync("sku-9", CancellationToken.None);
    Verify(missing.Error is { Type: ErrorType.NotFound, Code: "inventory.sku_not_found" }, "a 404 problem reads back as the service's NotFound error");

    using IHost unreachable = BuildHost(new()
    {
        ["SharedKernel:Communication:Clients:inventory:BaseAddress"] = "http://127.0.0.1:1",
        ["SharedKernel:Communication:Clients:inventory:Retry:MaxRetryAttempts"] = "0",
        ["SharedKernel:Communication:Clients:pricing:Address"] = "http://pricing",
    });
    await unreachable.StartAsync();
    Result<StockLevel> down = await unreachable.Services.GetRequiredService<IInventoryClient>().GetStockAsync("sku-1", CancellationToken.None);
    Verify(down.Error.Code == CommunicationErrorCodes.Unreachable, "a refused connection is communication.unreachable, not an exception");

    await host.StopAsync();
    await unreachable.StopAsync();
    Console.WriteLine("Surface 4 PASS: failures come back as Error values with the service's own code.");
}

static Task Surface5_GrpcErrorsAndMoney()
{
    var status = new RpcStatus { Code = (int)StatusCode.FailedPrecondition, Message = "Price list closed." };
    status.Details.Add(Google.Protobuf.WellKnownTypes.Any.Pack(new ErrorInfo { Reason = "pricing.list_closed", Domain = "pricing" }));
    Error error = RpcStatusExtensions.ToRpcException(status).ToError();
    Verify(error is { Type: ErrorType.BusinessRule, Code: "pricing.list_closed", Message: "Price list closed." }, "a rich status reads back as the service's error");

    Money price = Money.Create(19.99m, Currency.Eur).Value;
    Verify(price.ToMoneyProto().ToMoney().Value == price, "Money round-trips through google.type.Money");
    Verify(0.9999999999m.ToMoneyProto("USD") is { Units: 1, Nanos: 0 }, "a sub-nano fraction carries into units");

    Console.WriteLine("Surface 5 PASS: gRPC statuses and money convert without loss.");
    return Task.CompletedTask;
}

static IHost BuildHost(Dictionary<string, string?> settings, HttpMessageHandler? connection = null)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddInMemoryCollection(settings);

    builder.Services.AddSharedKernelCommunication(builder.Configuration)
        .AddRestClient<IInventoryClient, InventoryClient>("inventory", client =>
        {
            if (connection is not null)
            {
                client.HttpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => connection);
            }
        })
        .AddGrpcClient<SampleGrpcClient>("pricing");

    return builder.Build();
}

static void Verify(bool condition, string label)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {label}");
    }

    Console.WriteLine($"  - {label}");
}

internal sealed record StockLevel(string Sku, int Available);

internal interface IInventoryClient
{
    Task<Result<StockLevel>> GetStockAsync(string sku, CancellationToken cancellationToken);
}

internal sealed class InventoryClient(HttpClient http) : IInventoryClient
{
    public Task<Result<StockLevel>> GetStockAsync(string sku, CancellationToken cancellationToken) =>
        http.GetResultAsync<StockLevel>($"stock/{Uri.EscapeDataString(sku)}", cancellationToken);
}

/// <summary>Grpc.Net.ClientFactory activates a generated client through its CallInvoker constructor.</summary>
internal sealed class SampleGrpcClient(CallInvoker callInvoker)
{
    public CallInvoker CallInvoker { get; } = callInvoker;
}

/// <summary>Stands in for the network: records the last request and answers it.</summary>
internal sealed class RecordingConnection(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public HttpRequestMessage? Last { get; private set; }

    // Copied when sent: service discovery puts the original address back on the request once the call is over.
    public Uri? LastUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Last = request;
        LastUri = request.RequestUri;
        return Task.FromResult(respond(request));
    }
}
