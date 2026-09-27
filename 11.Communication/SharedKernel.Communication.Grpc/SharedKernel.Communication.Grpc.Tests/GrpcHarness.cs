using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.Grpc.Tests.Probe;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Communication;
using ProbeClient = SharedKernel.Communication.Grpc.Tests.Probe.Probe.ProbeClient;

namespace SharedKernel.Communication.Grpc.Tests;

/// <summary>
/// A real gRPC service on <see cref="TestServer"/> and a service with one client for it, "probe", registered through
/// <c>AddGrpcClient</c>: every call runs the client's whole pipeline and reaches the service in-process.
/// </summary>
internal sealed class GrpcHarness : IAsyncDisposable
{
    public const string ClientName = "probe";

    private readonly WebApplication _server;
    private readonly ServiceProvider _client;

    private GrpcHarness(WebApplication server, ServiceProvider client)
    {
        _server = server;
        _client = client;
    }

    public ProbeClient Client => _client.GetRequiredService<ProbeClient>();

    public static async Task<GrpcHarness> StartAsync(
        IDictionary<string, string?>? settings = null,
        Action<IGrpcClientBuilder>? configure = null,
        Func<HttpMessageHandler>? connection = null,
        Action<IServiceCollection>? services = null)
    {
        var serverBuilder = WebApplication.CreateBuilder();
        serverBuilder.WebHost.UseTestServer();
        serverBuilder.Services.AddGrpc();
        var server = serverBuilder.Build();
        server.MapGrpcService<ProbeService>();
        await server.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"SharedKernel:Communication:Clients:{ClientName}:Address"] = "http://probe" })
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSharedKernelCommunication(configuration).AddGrpcClient<ProbeClient>(ClientName, client =>
        {
            configure?.Invoke(client);
            client.HttpClientBuilder.ConfigurePrimaryHttpMessageHandler(connection ?? (() => new ResponseVersionHandler
            {
                InnerHandler = server.GetTestServer().CreateHandler(),
            }));
        });
        services?.Invoke(collection);

        var provider = collection.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
        return new GrpcHarness(server, provider);
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        await _server.DisposeAsync();
    }

    /// <summary>gRPC over <see cref="TestServer"/> needs the response version to match the request's.</summary>
    private sealed class ResponseVersionHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            response.Version = request.Version;
            return response;
        }
    }
}

/// <summary>Echoes what a call carried; fails on request.</summary>
internal sealed class ProbeService : Probe.Probe.ProbeBase
{
    private static readonly ConcurrentDictionary<string, int> Attempts = new();

    public override async Task<EchoReply> Echo(EchoRequest request, ServerCallContext context)
    {
        int attempt = Attempts.AddOrUpdate(request.Text, 1, (_, n) => n + 1);
        if (attempt <= request.FailAttempts)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "not yet"));
        }

        switch (request.Text)
        {
            case "not-found":
                throw GrpcCalls.ToRpcException(Error.NotFound("inventory.sku_not_found", "No such SKU."));
            case "invalid":
                throw GrpcCalls.ToRpcException(Error.Validation(
                [
                    Error.Validation("quantity.positive", "Quantity must be positive.") with
                    {
                        MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = "quantity" },
                    },
                ]));
            case "bare":
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Not now."));
            case "slow":
                await Task.Delay(TimeSpan.FromSeconds(10), context.CancellationToken);
                break;
        }

        var reply = new EchoReply
        {
            Text = request.Text,
            Attempt = attempt,
            DeadlineSeconds = context.Deadline == DateTime.MaxValue ? 0 : (context.Deadline - DateTime.UtcNow).TotalSeconds,
        };

        foreach (Metadata.Entry entry in context.RequestHeaders)
        {
            if (!entry.IsBinary)
            {
                reply.Metadata[entry.Key] = entry.Value;
            }
        }

        return reply;
    }
}
