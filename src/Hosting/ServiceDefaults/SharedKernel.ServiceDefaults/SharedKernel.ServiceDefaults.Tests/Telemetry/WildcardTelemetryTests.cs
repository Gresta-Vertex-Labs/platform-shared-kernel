using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// A host built with only <c>AddServiceDefaults()</c> exports every <c>SharedKernel.*</c> source and meter, and nothing
/// outside that prefix. Every source and meter below is created after the host's providers were built, which proves
/// nothing is enumerated up front.
/// </summary>
[Collection(NonParallelTelemetryCollection.Name)]
public sealed class WildcardTelemetryTests
{
    [Fact]
    public void SourceNamedSharedKernelAnything_ExportsASpan_AndNamesOutsideThePrefixDoNot()
    {
        using var host = TelemetryHarness.ForServiceDefaults();

        TelemetryHarness.EmitSpan("SharedKernel.Anything");
        TelemetryHarness.EmitSpan("Other.Anything");
        TelemetryHarness.EmitSpan("SharedKernel");
        TelemetryHarness.EmitSpan("OtherVendor.SharedKernel.X");

        Assert.Equal(["SharedKernel.Anything"], host.SpanSourceNames);
    }

    [Fact]
    public void SourceWithANestedSegment_ExportsASpan()
    {
        using var host = TelemetryHarness.ForServiceDefaults();

        TelemetryHarness.EmitSpan("SharedKernel.Persistence.EfCore.Auditing");

        Assert.Equal(["SharedKernel.Persistence.EfCore.Auditing"], host.SpanSourceNames);
    }

    [Fact]
    public void MeterNamedSharedKernelAnything_ExportsAMeasurement_AndNamesOutsideThePrefixDoNot()
    {
        using var host = TelemetryHarness.ForServiceDefaults();
        var probed = new[] { "SharedKernel.Anything", "Other.Anything", "SharedKernel", "OtherVendor.SharedKernel.X" };

        foreach (var name in probed)
        {
            using var meter = new Meter(name);
            meter.CreateCounter<long>("wildcard.test.count").Add(1);
        }

        host.Flush();

        Assert.Equal(["SharedKernel.Anything"], host.MeterNames.Where(probed.Contains));
    }

    [Fact]
    public void MeterWithANestedSegment_ExportsAMeasurement()
    {
        using var host = TelemetryHarness.ForServiceDefaults();

        using var meter = new Meter("SharedKernel.Persistence.EfCore.Encryption");
        meter.CreateCounter<long>("wildcard.test.count").Add(1);
        host.Flush();

        Assert.Contains("SharedKernel.Persistence.EfCore.Encryption", host.MeterNames);
    }

    [Fact]
    public void TheTwoNamesTheOldRegistrationsDropped_ExportWithNoWithXTelemetryCall()
    {
        using var host = TelemetryHarness.ForServiceDefaults();

        TelemetryHarness.EmitSpan("SharedKernel.Integration");
        using var meter = new Meter("SharedKernel.Messaging");
        meter.CreateCounter<long>("wildcard.test.count").Add(1);
        host.Flush();

        Assert.Contains("SharedKernel.Integration", host.SpanSourceNames);
        Assert.Contains("SharedKernel.Messaging", host.MeterNames);
    }

    [Fact]
    public void SignalRServerSource_ExportsASpan()
    {
        using var host = TelemetryHarness.ForServiceDefaults();

        // The name is read from Microsoft.AspNetCore.SignalR.Core of the .NET 10 shared framework.
        TelemetryHarness.EmitSpan("Microsoft.AspNetCore.SignalR.Server", "Microsoft.AspNetCore.SignalR.Server.OnConnected");

        Assert.Contains("Microsoft.AspNetCore.SignalR.Server", host.SpanSourceNames);
        Assert.Equal("Microsoft.AspNetCore.SignalR.Server", TelemetryNames.SignalRServerSource);
    }

    [Fact]
    public async Task HttpClientRequest_ExportsTheDurationMetricInSeconds()
    {
        using var host = TelemetryHarness.ForServiceDefaults();

        // A real socket: TestServer bypasses the sockets handler and emits no client metrics.
        var serverBuilder = WebApplication.CreateSlimBuilder();
        serverBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = serverBuilder.Build();
        server.MapGet("/ping", () => "pong");
        await server.StartAsync();

        using var client = new HttpClient(new SocketsHttpHandler());
        using var response = await client.GetAsync($"{server.Urls.First()}/ping");
        response.EnsureSuccessStatusCode();

        host.Flush();

        var duration = Assert.Single(host.Metrics, m => m.Name == "http.client.request.duration");
        Assert.Equal("s", duration.Unit);
    }
}
