using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D2/D16: <see cref="Result"/> and <see cref="Result{T}"/> map to typed results (and MVC action results) whose
/// success response is inferred by OpenAPI from endpoint metadata, with the <see cref="Task"/> forms alike.
/// </summary>
public sealed class ResultMappingTests : IClassFixture<FullStackHost>, IAsyncLifetime
{
    private static readonly Order SampleOrder = new("7", 3);

    private readonly FullStackHost _host;
    private WebApplication? _app;

    public ResultMappingTests(FullStackHost host)
    {
        _host = host;
    }

    private HttpClient Client => _app!.GetTestClient();

    public async Task InitializeAsync()
    {
        _app = await WebApiTestHost.StartAsync(app =>
        {
            app.MapGet("/ok", () => Result<Order>.Success(SampleOrder).ToOk());
            app.MapGet("/ok-mapped", () => Result<Order>.Success(SampleOrder).ToOk(order => order.Id));
            app.MapGet("/ok-async", () => Task.FromResult(Result<Order>.Success(SampleOrder)).ToOk());
            app.MapGet("/ok-async-failure", () => Task.FromResult(Result<Order>.Failure(TestErrors.OrderNotFound)).ToOk());
            app.MapGet("/etag", () => Result<Order>.Success(SampleOrder).ToOkWithETag(order => order.Version.ToString()));
            app.MapGet("/etag-async", () => Task.FromResult(Result<Order>.Success(SampleOrder)).ToOkWithETag(order => order.Id, order => order.Version.ToString()));
            app.MapPost("/created", () => Result<Order>.Success(SampleOrder).ToCreated(order => $"/orders/{order.Id}"));
            app.MapPost("/created-mapped", () => Task.FromResult(Result<Order>.Success(SampleOrder)).ToCreated(order => $"/orders/{order.Id}", order => order.Id));
            app.MapPost("/accepted", () => Result<Order>.Success(SampleOrder).ToAccepted());
            app.MapPost("/accepted-location", () => Task.FromResult(Result<Order>.Success(SampleOrder)).ToAccepted(order => $"/jobs/{order.Id}"));
            app.MapDelete("/no-content", () => Result.Success().ToNoContent());
            app.MapDelete("/no-content-async", () => Task.FromResult(Result.Success()).ToNoContent());
            app.MapDelete("/no-content-value", () => Result<Order>.Success(SampleOrder).ToNoContent());
            app.MapDelete("/no-content-value-async", () => Task.FromResult(Result<Order>.Failure(TestErrors.OrderNotFound)).ToNoContent());
            app.MapGet("/custom", () => Result<Order>.Success(SampleOrder).ToHttpResult(order => TypedResults.Text(order.Id)));
            app.MapGet("/custom-async", () => Task.FromResult(Result.Success()).ToHttpResult(() => TypedResults.Text("done")));
            app.MapGet("/custom-failure", () => Result.Failure(TestErrors.OrderNotFound).ToHttpResult(() => TypedResults.Text("done")));
            app.MapGet("/error", () => TestErrors.OrderNotFound.ToErrorResult());
        });
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("/ok")]
    [InlineData("/ok-async")]
    public async Task ToOk_Is200_WithTheValue(string path)
    {
        using var response = await Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<Order>()).Should().Be(SampleOrder);
    }

    [Fact]
    public async Task ToOkWithMap_Is200_WithTheMappedValue()
    {
        using var response = await Client.GetAsync("/ok-mapped");

        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("7");
    }

    [Theory]
    [InlineData("/ok-async-failure")]
    [InlineData("/error")]
    public async Task Failure_IsTheProblem(string path)
    {
        using var response = await Client.GetAsync(path);

        await response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, "order.not_found");
    }

    [Theory]
    [InlineData("/etag")]
    [InlineData("/etag-async")]
    public async Task ToOkWithETag_SendsTheVersion(string path)
    {
        using var response = await Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag!.Tag.Should().Be("\"3\"");
    }

    [Fact]
    public async Task ToCreated_Is201_WithLocation_AndTheValue()
    {
        using var response = await Client.PostAsync("/created", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().Be(new Uri("/orders/7", UriKind.Relative));
        (await response.Content.ReadFromJsonAsync<Order>()).Should().Be(SampleOrder);
    }

    [Fact]
    public async Task ToCreatedWithMap_Is201_WithTheMappedValue()
    {
        using var response = await Client.PostAsync("/created-mapped", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("7");
    }

    [Fact]
    public async Task ToAccepted_Is202_WithOrWithoutLocation()
    {
        using var withoutLocation = await Client.PostAsync("/accepted", content: null);
        using var withLocation = await Client.PostAsync("/accepted-location", content: null);

        withoutLocation.StatusCode.Should().Be(HttpStatusCode.Accepted);
        withoutLocation.Headers.Location.Should().BeNull();
        withLocation.StatusCode.Should().Be(HttpStatusCode.Accepted);
        withLocation.Headers.Location.Should().Be(new Uri("/jobs/7", UriKind.Relative));
    }

    [Theory]
    [InlineData("/no-content")]
    [InlineData("/no-content-async")]
    [InlineData("/no-content-value")]
    public async Task ToNoContent_Is204(string path)
    {
        using var response = await Client.DeleteAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ToNoContent_OfAFailure_IsTheProblem()
    {
        using var response = await Client.DeleteAsync("/no-content-value-async");

        await response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, "order.not_found");
    }

    [Theory]
    [InlineData("/custom", "7")]
    [InlineData("/custom-async", "done")]
    public async Task ToHttpResult_UsesTheSuccessResult(string path, string expected)
    {
        using var response = await Client.GetAsync(path);

        (await response.Content.ReadAsStringAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task ToHttpResult_OfAFailure_IsTheProblem()
    {
        using var response = await Client.GetAsync("/custom-failure");

        await response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, "order.not_found");
    }

    [Fact]
    public void TypedResult_ExposesTheErrorResult_ForUnitTests()
    {
        var failure = Result<Order>.Failure(TestErrors.OrderNotFound).ToOk();
        var success = Result<Order>.Success(SampleOrder).ToOk();

        var error = failure.Result.Should().BeOfType<ErrorHttpResult>().Subject;
        error.Error.Should().Be(TestErrors.OrderNotFound);
        error.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        error.ContentType.Should().Be("application/problem+json");
        success.Result.Should().BeOfType<Ok<Order>>().Which.Value.Should().Be(SampleOrder);
    }

    [Fact]
    public void ToErrorResult_RefusesErrorNone()
    {
        var act = () => Error.None.ToErrorResult();

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("/ok", 200, typeof(Order))]
    [InlineData("/ok-async", 200, typeof(Order))]
    [InlineData("/ok-mapped", 200, typeof(string))]
    [InlineData("/created", 201, typeof(Order))]
    [InlineData("/no-content", 204, typeof(void))]
    [InlineData("/etag", 200, typeof(Order))]
    [InlineData("/etag", 304, typeof(void))]
    [InlineData("/etag-async", 200, typeof(string))]
    public void TypedResults_LetOpenApiInferTheSuccessResponse(string route, int statusCode, Type type)
    {
        var endpoint = _app!.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == route);

        endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Should().Contain(metadata => metadata.StatusCode == statusCode && metadata.Type == type);
    }

    [Fact]
    public async Task Mvc_Success_MapsTo200_204_And201()
    {
        using var ok = await _host.Client.GetAsync("/mvc-api/ok");
        using var done = await _host.Client.DeleteAsync("/mvc-api/done");
        using var created = await _host.Client.PostAsync("/mvc-api/created", content: null);

        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).Should().Contain("value");
        done.StatusCode.Should().Be(HttpStatusCode.NoContent);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public void OkWithETag_RefusesAVersionThatCannotBeAnEntityTag()
    {
        var act = () => new OkWithETag<Order>(SampleOrder, "a\"b");

        act.Should().Throw<ArgumentException>();
    }

    public sealed record Order(string Id, int Version);
}
