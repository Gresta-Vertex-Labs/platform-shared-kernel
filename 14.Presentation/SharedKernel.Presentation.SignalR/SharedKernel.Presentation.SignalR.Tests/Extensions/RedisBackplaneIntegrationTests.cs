using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Presentation.SignalR.Extensions;
using SharedKernel.Testing.Containers;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Extensions;

/// <summary>
/// Testcontainers-backed integration test proving that two independently-configured SignalR server
/// instances, both opted into <see cref="RedisBackplaneExtensions.WithRedisBackplane"/> against the
/// same Redis instance, fan out group/broadcast messages across instances — the scale-out scenario
/// the backplane exists to support.
/// </summary>
/// <remarks>
/// Uses a real Redis container via <see cref="RedisContainerFixture"/> (16.Testing) — no mocked
/// Redis connection, per the platform's Testcontainers-required rule for this scenario.
/// </remarks>
[Collection("RedisBackplane")]
public sealed class RedisBackplaneIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainerFixture _redisFixture = new();
    private TestServer? _serverA;
    private TestServer? _serverB;

    public async Task InitializeAsync() => await _redisFixture.InitializeAsync();

    public async Task DisposeAsync()
    {
        _serverA?.Dispose();
        _serverB?.Dispose();
        await _redisFixture.DisposeAsync();
    }

    [Fact]
    public async Task MessageSentThroughOneHubContext_IsReceivedByClientConnectedThroughTheOtherInstance()
    {
        _serverA = CreateServer();
        _serverB = CreateServer();

        await using var connection = new HubConnectionBuilder()
            .WithUrl(
                "http://localhost/echo",
                options => options.HttpMessageHandlerFactory = _ => _serverB.CreateHandler())
            .Build();

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<string>("ReceiveMessage", message => tcs.TrySetResult(message));

        await connection.StartAsync();

        // Give the backplane subscription a moment to fully establish across both server instances.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        var hubContext = _serverA.Services.GetRequiredService<IHubContext<EchoHub>>();
        await hubContext.Clients.All.SendAsync("ReceiveMessage", "hello-from-instance-a");

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(15)));

        completed.Should().Be(tcs.Task, "the message should have been delivered via the shared Redis backplane within the timeout");
        var received = await tcs.Task;
        received.Should().Be("hello-from-instance-a");
    }

    private TestServer CreateServer()
    {
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services
                        .AddSharedKernelSignalR()
                        .WithRedisBackplane(_redisFixture.ConnectionString);
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapHub<EchoHub>("/echo"));
                });
            });

        var host = hostBuilder.Start();
        return host.GetTestServer();
    }

    private sealed class EchoHub : Hub;
}
