using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace OrderApi.Tests;

/// <summary>The real <c>Program</c> of <c>OrderApi.Api</c> over HTTP.</summary>
public sealed class HttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string CorrelationHeader = "X-Correlation-Id";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PlaceThenGet_RoundTrips()
    {
        var created = await _client.PostAsJsonAsync("/orders",
            new { customer = "Acme Ltd", amount = 149.50m, currency = "eur", lines = new[] { "Widget x2" } });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        created.Headers.Location.Should().Be($"/orders/{id}");

        var fetched = await _client.GetAsync($"/orders/{id}");

        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await fetched.Content.ReadFromJsonAsync<JsonElement>();
        order.GetProperty("customer").GetString().Should().Be("Acme Ltd");
        order.GetProperty("currency").GetString().Should().Be("EUR");
    }

    [Fact]
    public async Task InvalidOrder_IsAValidationProblem_WithEveryField()
    {
        var response = await _client.PostAsJsonAsync("/orders",
            new { customer = "", amount = 10m, currency = "", lines = Array.Empty<string>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errorCode").GetString().Should().Be("validation.failed");
        problem.GetProperty("errors").EnumerateObject().Select(e => e.Name)
            .Should().BeEquivalentTo(["Customer", "Currency", "Lines"]);
    }

    [Fact]
    public async Task UnknownOrder_IsNotFound()
    {
        var response = await _client.GetAsync($"/orders/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString()
            .Should().Be("order.notFound");
    }

    [Fact]
    public async Task CorrelationId_IsEchoed_AndCreatedWhenAbsent()
    {
        using var withId = new HttpRequestMessage(HttpMethod.Get, $"/orders/{Guid.CreateVersion7()}");
        withId.Headers.Add(CorrelationHeader, "order-test-42");
        var echoed = await _client.SendAsync(withId);

        echoed.Headers.GetValues(CorrelationHeader).Should().ContainSingle().Which.Should().Be("order-test-42",
            "UseSharedKernelRequestContext() runs first, so even an error response carries the caller's correlation id");

        var created = await _client.GetAsync("/health/live");
        created.Headers.GetValues(CorrelationHeader).Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task OpenApiDocument_IsServedInDevelopment()
    {
        // WebApplicationFactory runs the host in Development, where AddSharedKernelOpenApi publishes the documents.
        // Outside Development nothing is mapped: CI's smoke test runs the host in Production and expects a 404.
        var response = await _client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        document.GetProperty("paths").EnumerateObject().Select(p => p.Name)
            .Should().Contain(["/orders", "/orders/{id}", "/orders/{id}/cancel"]);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthEndpoints_AreHealthy(string path)
    {
        var response = await _client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Healthy");
    }
}
