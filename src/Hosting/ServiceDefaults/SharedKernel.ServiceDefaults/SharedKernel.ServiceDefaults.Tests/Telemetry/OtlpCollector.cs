using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// A loopback OTLP/HTTP collector: records the path and body of every export request, so a test sees which signals the
/// real exporter actually sent.
/// </summary>
internal sealed class OtlpCollector : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<(string Path, byte[] Body)> _requests = new();

    private OtlpCollector(WebApplication app, string url)
    {
        _app = app;
        Url = url;
    }

    /// <summary>The base address, without a trailing slash.</summary>
    public string Url { get; }

    /// <summary>The distinct paths received so far (<c>/v1/traces</c>, <c>/v1/metrics</c>, <c>/v1/logs</c>).</summary>
    public IReadOnlySet<string> Paths => _requests.Select(request => request.Path).ToHashSet(StringComparer.Ordinal);

    /// <summary>The bodies received on <paramref name="path"/>.</summary>
    public IEnumerable<byte[]> BodiesOf(string path) => _requests.Where(request => request.Path == path).Select(request => request.Body);

    /// <summary>Starts a collector on a free loopback port.</summary>
    public static async Task<OtlpCollector> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        OtlpCollector? collector = null;

        app.MapPost(
            "/{**path}",
            async (HttpContext context) =>
            {
                using var body = new MemoryStream();
                await context.Request.Body.CopyToAsync(body);
                collector!._requests.Enqueue((context.Request.Path.Value!, body.ToArray()));
                return Results.Ok();
            });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        collector = new OtlpCollector(app, address.TrimEnd('/'));
        return collector;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
