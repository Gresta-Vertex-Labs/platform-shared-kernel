using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D4/D6/D16 on a real Kestrel listener (the in-memory test server enforces neither body limits nor the
/// <c>Server</c> header): the service-wide request body limit answered 413 <c>request.too_large</c>, the per-endpoint
/// overrides, and no <c>Server</c> header.
/// </summary>
public sealed class KestrelLimitsTests
{
    private const int GlobalLimit = 1024;

    private static readonly string OversizedJson = $"{{\"name\":\"{new string('x', 4 * GlobalLimit)}\"}}";

    [Theory]
    [InlineData("/raw", true, WebApiTestHost.Production)]
    [InlineData("/raw", false, WebApiTestHost.Production)]
    [InlineData("/json", true, WebApiTestHost.Production)]
    [InlineData("/json", false, WebApiTestHost.Production)]
    [InlineData("/json", true, WebApiTestHost.Development)]
    public async Task BodyOverTheLimit_Is413_RequestTooLarge_BeforeTheHandlerFinishes(string path, bool chunked, string environment)
    {
        var handled = false;
        await using var app = await StartAsync(onHandled: () => handled = true, environment: environment);
        using var client = new HttpClient { BaseAddress = app.GetKestrelAddress() };
        using var request = new HttpRequestMessage(HttpMethod.Post, path.TrimStart('/'))
        {
            Content = new StringContent(OversizedJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.TransferEncodingChunked = chunked;

        using var response = await client.SendAsync(request);

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status413PayloadTooLarge, PresentationErrorCodes.RequestTooLarge);
        problem.GetProperty("title").GetString().Should().Be("Content Too Large");
        handled.Should().BeFalse();
    }

    [Theory]
    [InlineData("/raised")]
    [InlineData("/unlimited")]
    public async Task EndpointOverride_AcceptsALargerBody(string path)
    {
        var handled = false;
        await using var app = await StartAsync(onHandled: () => handled = true);
        using var client = new HttpClient { BaseAddress = app.GetKestrelAddress() };
        using var content = new StringContent(OversizedJson, Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(path.TrimStart('/'), content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handled.Should().BeTrue();
    }

    [Fact]
    public async Task BodyWithinTheLimit_IsAccepted()
    {
        await using var app = await StartAsync(onHandled: () => { });
        using var client = new HttpClient { BaseAddress = app.GetKestrelAddress() };
        using var content = new StringContent("{\"name\":\"small\"}", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("json", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ServerHeader_IsNotSent()
    {
        await using var app = await StartAsync(onHandled: () => { });
        using var client = new HttpClient { BaseAddress = app.GetKestrelAddress() };

        using var response = await client.GetAsync("ping");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains(HeaderNames.Server).Should().BeFalse();
    }

    [Fact]
    public async Task ServerHeader_IsSent_WhenRemoveServerHeaderIsOff()
    {
        await using var app = await StartAsync(onHandled: () => { }, configure: options => options.RemoveServerHeader = false);
        using var client = new HttpClient { BaseAddress = app.GetKestrelAddress() };

        using var response = await client.GetAsync("ping");

        response.Headers.Server.ToString().Should().Be("Kestrel");
    }

    private static Task<WebApplication> StartAsync(
        Action onHandled,
        string environment = WebApiTestHost.Production,
        Action<SharedKernelWebApiOptions>? configure = null) =>
        WebApiTestHost.StartKestrelAsync(
            app =>
            {
                app.MapGet("/ping", () => "pong");
                app.MapPost("/raw", async (HttpRequest request) =>
                {
                    using var reader = new StreamReader(request.Body);
                    var body = await reader.ReadToEndAsync();
                    onHandled();
                    return body.Length;
                });
                app.MapPost("/json", ([FromBody] Payload payload) =>
                {
                    onHandled();
                    return payload.Name.Length;
                });
                app.MapPost("/raised", ([FromBody] Payload payload) =>
                {
                    onHandled();
                    return payload.Name.Length;
                }).WithRequestSizeLimit(16 * GlobalLimit);
                app.MapPost("/unlimited", ([FromBody] Payload payload) =>
                {
                    onHandled();
                    return payload.Name.Length;
                }).DisableRequestSizeLimit();
            },
            configureOptions: options =>
            {
                options.Limits.MaxRequestBodySize = GlobalLimit;
                configure?.Invoke(options);
            },
            environment: environment);

    public sealed record Payload(string Name);
}
