using System.Net.WebSockets;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.SignalR.Options;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Presentation.SignalR.Tests.TestSupport;

/// <summary>
/// Builds real in-process hosts the way a service does and opens live <see cref="HubConnection"/>s to them over
/// <see cref="TestServer"/> — WebSockets by default, long polling on request.
/// </summary>
internal static class SignalRTestHost
{
    public const string Production = "Production";

    public const string Development = "Development";

    /// <summary>
    /// Starts a <see cref="TestServer"/> host: test authentication, <c>AddSharedKernelWebApi</c> (unless
    /// <paramref name="withWebApi"/> is false), <c>AddSharedKernelSignalR</c> (unless
    /// <paramref name="withSharedKernelSignalR"/> is false, when <paramref name="configureBuilder"/> registers SignalR
    /// itself), <paramref name="configureBuilder"/>, then <c>UseSharedKernelWebApi()</c> (or plain routing,
    /// authentication and authorization) and <paramref name="mapHubs"/>.
    /// </summary>
    public static async Task<WebApplication> StartAsync(
        Action<WebApplication> mapHubs,
        Action<SharedKernelSignalROptions>? configureSignalR = null,
        Action<WebApplicationBuilder>? configureBuilder = null,
        string environment = Production,
        IReadOnlyDictionary<string, string?>? configuration = null,
        InMemoryLoggerFactory? loggerFactory = null,
        bool withWebApi = true,
        bool withSharedKernelSignalR = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        if (configuration is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuration);
        }

        if (loggerFactory is not null)
        {
            builder.Services.AddSingleton<ILoggerFactory>(loggerFactory);
        }

        builder.AddTestAuthentication();
        builder.Services.AddSingleton<InvocationCounter>();

        if (withWebApi)
        {
            builder.AddSharedKernelWebApi();
        }

        if (withSharedKernelSignalR)
        {
            builder.AddSharedKernelSignalR(configureSignalR);
        }

        configureBuilder?.Invoke(builder);

        var app = builder.Build();

        if (withWebApi)
        {
            app.UseSharedKernelWebApi();
        }
        else
        {
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
        }

        mapHubs(app);

        try
        {
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    /// <summary>Opens a connection to the hub at <paramref name="path"/>, sending <paramref name="headers"/> on every request.</summary>
    public static async Task<HubConnection> ConnectAsync(
        this WebApplication app,
        string path,
        IReadOnlyDictionary<string, string>? headers = null,
        HttpTransportType transport = HttpTransportType.WebSockets)
    {
        var connection = app.CreateConnection(path, headers, transport);

        try
        {
            await connection.StartAsync();
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        return connection;
    }

    /// <summary>Builds, but does not start, a connection to the hub at <paramref name="path"/>.</summary>
    public static HubConnection CreateConnection(
        this WebApplication app,
        string path,
        IReadOnlyDictionary<string, string>? headers = null,
        HttpTransportType transport = HttpTransportType.WebSockets)
    {
        var server = app.GetTestServer();

        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, path), options =>
            {
                options.Transports = transport;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.WebSocketFactory = (context, cancellationToken) => ConnectWebSocketAsync(server, context, cancellationToken);

                if (headers is not null)
                {
                    foreach (var (name, value) in headers)
                    {
                        options.Headers[name] = value;
                    }
                }
            })
            .Build();
    }

    /// <summary>
    /// Returns the message the server put in its <see cref="HubException"/>. SignalR prefixes it on the wire with
    /// "An unexpected error occurred invoking '…' on the server. HubException: ".
    /// </summary>
    public static string ServerMessage(this HubException exception)
    {
        const string Marker = "HubException: ";

        var index = exception.Message.IndexOf(Marker, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, "the server answered with a HubException, but the message was: {0}", exception.Message);

        return exception.Message[(index + Marker.Length)..];
    }

    /// <summary>Invokes <paramref name="method"/> expecting a failure and returns the message the server sent.</summary>
    public static async Task<string> InvokeExpectingErrorAsync(this HubConnection connection, string method, params object?[] arguments)
    {
        var act = () => connection.InvokeCoreAsync<object?>(method, arguments);

        var exception = await act.Should().ThrowAsync<HubException>();
        return exception.Which.ServerMessage();
    }

    /// <summary>
    /// The message SignalR itself sends when the caller does not satisfy the authorization attributes of hub method
    /// <paramref name="method"/> (ASP.NET Core 10.0.11). SignalR authorizes before any hub filter runs, so the message
    /// is SignalR's, never the platform's <c>"{code}: {message}"</c>, and names neither the caller nor the requirement.
    /// </summary>
    public static string UnauthorizedMessage(string method) => $"Failed to invoke '{method}' because user is unauthorized";

    /// <summary>
    /// Invokes <paramref name="method"/> expecting SignalR to refuse it for authorization, and returns the message of
    /// the <see cref="HubException"/> the call failed with, as SignalR sent it.
    /// </summary>
    public static async Task<string> InvokeExpectingRefusalAsync(this HubConnection connection, string method)
    {
        var act = () => connection.InvokeCoreAsync<object?>(method, []);

        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Be(UnauthorizedMessage(method));
        return exception.Which.Message;
    }

    /// <summary>Waits until <paramref name="condition"/> holds, for things the server does after answering.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    // TestServer serves WebSockets through its own client; the connection's headers go on the upgrade request.
    private static async ValueTask<WebSocket> ConnectWebSocketAsync(
        TestServer server,
        WebSocketConnectionContext context,
        CancellationToken cancellationToken)
    {
        var client = server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            foreach (var (name, value) in context.Options.Headers)
            {
                request.Headers[name] = value;
            }
        };

        return await client.ConnectAsync(context.Uri, cancellationToken);
    }
}
