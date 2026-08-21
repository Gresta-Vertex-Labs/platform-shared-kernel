using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.PayloadLimits;
using Xunit;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Integration tests for the payload-size/JSON-depth denial-of-service protection capability
/// (<see cref="PayloadLimitsExtensions"/>, WO-063 P-411).
/// </summary>
public sealed class PayloadLimitsIntegrationTests
{
    /// <summary>
    /// T-44: proves the maximum-request-body-size limit is enforced against a REAL listening
    /// Kestrel host — <c>WebApplicationFactory</c>'s in-memory <c>TestServer</c> does not enforce
    /// <see cref="Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature"/> the same way
    /// a real Kestrel connection does, per this capability's own documented verification discipline.
    /// </summary>
    [Fact]
    public async Task RequestBodyExceedingMaxSize_Rejected413_BeforeHandlerInvoked()
    {
        var handlerReached = false;

        await using var host = await StartRealKestrelHostAsync(app =>
        {
            app.UseExceptionHandler();
            app.UseSharedKernelPayloadLimits(options => options.MaxRequestBodySizeBytes = 64);

            app.MapPost("/upload", ([FromBody] UploadPayload payload) =>
            {
                handlerReached = true;
                return HttpResults.Ok();
            });
        });

        using var client = new HttpClient();
        var oversizedBody = new string('x', 4096);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{host.BaseAddress}upload")
        {
            Content = new StringContent($"{{\"data\":\"{oversizedBody}\"}}", Encoding.UTF8, "application/json"),
        };
        // Force chunked transfer encoding (no Content-Length header) so Kestrel must read the body
        // incrementally and throw BadHttpRequestException only once the actual byte count exceeds
        // the configured limit, giving SharedKernelExceptionHandler a chance to shape the response —
        // a Content-Length-declared oversized body is instead rejected by Kestrel at the connection
        // level before the middleware pipeline ever runs, producing an empty body (confirmed by a
        // real round trip against this exact host).
        request.Headers.TransferEncodingChunked = true;

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        handlerReached.Should().BeFalse("the handler must never be invoked when the body exceeds the configured limit");
    }

    /// <summary>Sanity/regression companion to T-44: a body within the limit reaches the handler normally.</summary>
    [Fact]
    public async Task RequestBodyWithinMaxSize_HandlerInvoked_Returns200()
    {
        var handlerReached = false;

        await using var host = await StartRealKestrelHostAsync(app =>
        {
            app.UseExceptionHandler();
            app.UseSharedKernelPayloadLimits(options => options.MaxRequestBodySizeBytes = 1_048_576);

            app.MapPost("/upload", ([FromBody] UploadPayload payload) =>
            {
                handlerReached = true;
                return HttpResults.Ok();
            });
        });

        using var client = new HttpClient();
        using var content = new StringContent("{\"data\":\"small\"}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"{host.BaseAddress}upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handlerReached.Should().BeTrue();
    }

    /// <summary>
    /// T-45 (regression half): a host that never calls
    /// <see cref="PayloadLimitsExtensions.UseSharedKernelPayloadLimits"/> is byte-identical to
    /// pre-P-411 behavior — no request-body-size ceiling is imposed beyond Kestrel's own
    /// framework default.
    /// </summary>
    [Fact]
    public async Task HostNeverConfiguresPayloadLimits_AcceptsBodyLargerThanPlatformDefault()
    {
        var handlerReached = false;

        await using var host = await StartRealKestrelHostAsync(app =>
        {
            // Deliberately no UseSharedKernelPayloadLimits() call.
            app.MapPost("/upload", ([FromBody] UploadPayload payload) =>
            {
                handlerReached = true;
                return HttpResults.Ok();
            });
        });

        using var client = new HttpClient();
        // Larger than PayloadLimitsOptions' own 1 MB default — proves no limit is silently applied.
        var largeBody = new string('y', 2 * 1024 * 1024);
        using var content = new StringContent($"{{\"data\":\"{largeBody}\"}}", Encoding.UTF8, "application/json");

        var response = await client.PostAsync($"{host.BaseAddress}upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handlerReached.Should().BeTrue();
    }

    private static async Task<RealKestrelHost> StartRealKestrelHostAsync(Action<WebApplication> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();

        var app = builder.Build();
        configure(app);

        await app.StartAsync();

        var addressesFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var baseAddress = addressesFeature!.Addresses.First();
        if (!baseAddress.EndsWith('/'))
        {
            baseAddress += "/";
        }

        return new RealKestrelHost(app, baseAddress);
    }

    private sealed class UploadPayload
    {
        public string? Data { get; set; }
    }

    private sealed class RealKestrelHost(WebApplication app, string baseAddress) : IAsyncDisposable
    {
        public string BaseAddress { get; } = baseAddress;

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    /// <summary>
    /// T-45 (violation half): a JSON payload exceeding the configured
    /// <see cref="PayloadLimitsOptions.MaxJsonDepth"/> is rejected with 400 via STJ's own existing
    /// exception path — this capability only wires the <c>MaxDepth</c> value, it invents no new
    /// depth-violation response shape. Uses <c>TestServer</c> (not a real Kestrel host) since this
    /// path is a model-binding/JSON-deserialization concern, not a Kestrel transport-level one.
    /// </summary>
    public sealed class JsonMaxDepthTests : IClassFixture<JsonMaxDepthTests.JsonMaxDepthFactory>
    {
        private readonly HttpClient _client;

        public JsonMaxDepthTests(JsonMaxDepthFactory factory) => _client = factory.CreateClient();

        [Fact]
        public async Task JsonPayloadExceedingMaxDepth_Returns400()
        {
            using var content = new StringContent(BuildNestedJson(depth: 40), Encoding.UTF8, "application/json");

            var response = await _client.PostAsync("/echo", content);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task JsonPayloadWithinMaxDepth_Returns200()
        {
            using var content = new StringContent(BuildNestedJson(depth: 5), Encoding.UTF8, "application/json");

            var response = await _client.PostAsync("/echo", content);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        private static string BuildNestedJson(int depth)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < depth; i++)
            {
                builder.Append("{\"n\":");
            }

            builder.Append("1");
            builder.Append(new string('}', depth));
            return builder.ToString();
        }

        public sealed class JsonMaxDepthFactory : WebApplicationFactory<JsonMaxDepthFactory>
        {
            protected override IHost CreateHost(IHostBuilder builder)
            {
                var appBuilder = WebApplication.CreateBuilder();
                appBuilder.WebHost.UseTestServer();

                appBuilder.Services.AddRouting();
                appBuilder.Services.AddSharedKernelPayloadLimits(options => options.MaxJsonDepth = 32);
                appBuilder.Services.AddProblemDetails();
                appBuilder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();

                var app = appBuilder.Build();
                app.UseExceptionHandler();

                app.MapPost("/echo", ([FromBody] System.Text.Json.Nodes.JsonNode node) => HttpResults.Ok());

                app.Start();
                return app;
            }
        }
    }
}
