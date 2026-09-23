using System.Globalization;
using System.Net;
using Asp.Versioning;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Versioning;

/// <summary>Sunset and deprecation policies declared through the Versioning hook, on the wire and in the documents.</summary>
public sealed class VersionLifecycleTests : IAsyncLifetime
{
    private static readonly DateTimeOffset DeprecatedOn = new(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);
    private static readonly DateTimeOffset SunsetOn = new(2027, 1, 31, 0, 0, 0, TimeSpan.Zero);

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _app = await OpenApiTestHost.StartAsync(
            OrdersApi.Map,
            options =>
            {
                options.Description = "Places and tracks orders.";
                options.Versioning = versioning =>
                {
                    versioning.Policies.Deprecate(1.0)
                        .Effective(DeprecatedOn)
                        .Link("https://docs.example.com/orders/v1-deprecation")
                        .Title("Version 1 deprecation")
                        .Type("text/html");
                    versioning.Policies.Sunset(1.0)
                        .Effective(SunsetOn)
                        .Link("https://docs.example.com/orders/v1-sunset")
                        .Title("Version 1 sunset")
                        .Type("text/html");
                };
            });
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task DeprecatedVersion_IsAnsweredWithAnRfc9745DeprecationDate()
    {
        using var response = await _client.GetAsync("/v1/orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Deprecation").Should().ContainSingle()
            .Which.Should().Be("@" + DeprecatedOn.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task SunsetVersion_IsAnsweredWithAnHttpDate()
    {
        using var response = await _client.GetAsync("/v1/orders");

        var sunset = response.Headers.GetValues("Sunset").Should().ContainSingle().Subject;
        sunset.Should().Be(SunsetOn.ToString("r", CultureInfo.InvariantCulture));
        DateTimeOffset.ParseExact(sunset, "r", CultureInfo.InvariantCulture).Should().Be(SunsetOn);
    }

    [Fact]
    public async Task PolicyLinks_AreAnsweredAsLinkHeaders()
    {
        using var response = await _client.GetAsync("/v1/orders");

        var links = string.Join(", ", response.Headers.GetValues("Link"));
        links.Should().Contain("<https://docs.example.com/orders/v1-deprecation>").And.Contain("rel=\"deprecation\"");
        links.Should().Contain("<https://docs.example.com/orders/v1-sunset>").And.Contain("rel=\"sunset\"");
    }

    [Fact]
    public async Task CurrentVersion_CarriesNoLifecycleHeaders_ButReportsTheVersions()
    {
        using var response = await _client.GetAsync("/v2/orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("Deprecation").Should().BeFalse();
        response.Headers.Contains("Sunset").Should().BeFalse();
        response.Headers.Contains("Link").Should().BeFalse();
        response.Headers.GetValues("api-supported-versions").Should().ContainSingle().Which.Should().Be("1.0, 2.0");
    }

    [Fact]
    public async Task DeprecatedVersionDocument_KeepsTheConfiguredDescription_AndAddsTheNotices()
    {
        var v1 = await _client.GetDocumentAsync("v1");
        var v2 = await _client.GetDocumentAsync("v2");

        var description = v1["info"]!["description"]!.GetValue<string>();
        description.Should().StartWith("Places and tracks orders.\n\n");
        description.Should().Contain("deprecated").And.Contain("https://docs.example.com/orders/v1-sunset");
        v2["info"]!["description"]!.GetValue<string>().Should().Be("Places and tracks orders.");
    }
}
