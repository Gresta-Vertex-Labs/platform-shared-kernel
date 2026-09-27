using FluentAssertions;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Communication;

namespace SharedKernel.Communication.Testing.Tests.Communication;

public sealed class StubHttpMessageHandlerTests
{
    [Fact]
    public async Task A_route_answers_by_method_and_path_whatever_the_host()
    {
        var stub = new StubHttpMessageHandler().RespondJson(HttpMethod.Get, "/stock/sku-1", new { Sku = "sku-1" });
        using var client = new HttpClient(stub);

        string body = await client.GetStringAsync("http://any-host:1234/stock/sku-1?fresh=true");

        body.Should().Be("""{"sku":"sku-1"}""");
    }

    [Fact]
    public async Task The_route_added_last_wins_and_the_attempt_number_is_counted()
    {
        var stub = new StubHttpMessageHandler()
            .RespondStatus(HttpMethod.Get, "/x", HttpStatusCode.NotFound)
            .Respond(HttpMethod.Get, "/x", (_, n) => new HttpResponseMessage(n == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        using var client = new HttpClient(stub);

        HttpStatusCode first = (await client.GetAsync("http://h/x")).StatusCode;
        HttpStatusCode second = (await client.GetAsync("http://h/x")).StatusCode;

        first.Should().Be(HttpStatusCode.ServiceUnavailable);
        second.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Every_request_is_recorded_with_its_headers_and_body()
    {
        var stub = new StubHttpMessageHandler().RespondStatus(HttpMethod.Post, "/orders", HttpStatusCode.Created);
        using var client = new HttpClient(stub);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://h/orders") { Content = new StringContent("{\"id\":1}") };
        request.Headers.Add("X-Tenant-Id", "t1");

        await client.SendAsync(request);

        RecordedHttpRequest recorded = stub.Requests.Should().ContainSingle().Subject;
        recorded.Method.Should().Be(HttpMethod.Post);
        recorded.Header("x-tenant-id").Should().Be("t1");
        recorded.Body.Should().Be("{\"id\":1}");
    }

    [Fact]
    public async Task A_request_nothing_answers_fails_the_test()
    {
        using var client = new HttpClient(new StubHttpMessageHandler());

        Func<Task> call = () => client.GetAsync("http://h/missing");

        await call.Should().ThrowAsync<InvalidOperationException>().WithMessage("*GET /missing*");
    }

    [Fact]
    public async Task Under_a_typed_client_the_problem_reads_back_as_the_error()
    {
        var stub = new StubHttpMessageHandler().RespondProblem(
            HttpMethod.Post,
            "/reservations",
            HttpStatusCode.BadRequest,
            "validation.failed",
            fieldErrors: new Dictionary<string, string[]> { ["quantity"] = ["Must be positive."] });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCommunication(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["SharedKernel:Communication:Clients:inventory:BaseAddress"] = "http://inventory" })
                .Build())
            .AddRestClient<InventoryClient>("inventory");
        services.UseStubHttpMessageHandler("inventory", stub);
        using var provider = services.BuildServiceProvider();

        Result result = await provider.GetRequiredService<InventoryClient>().Http.PostResultAsync("reservations", new { quantity = 0 }, CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Details.Should().ContainSingle().Which.Code.Should().Be("quantity");
        stub.Requests.Single().Header("X-Correlation-Id").Should().NotBeNull("the client's own handlers still ran");
    }

    [Fact]
    public async Task A_thrown_failure_is_the_unreachable_service()
    {
        var stub = new StubHttpMessageHandler().Throw(HttpMethod.Get, "/x", new HttpRequestException("refused"));
        using var client = new HttpClient(stub);

        Result result = await client.SendResultAsync(new HttpRequestMessage(HttpMethod.Get, "http://h/x"));

        result.Error.Code.Should().Be(CommunicationErrorCodes.Unreachable);
    }

    public sealed class InventoryClient(HttpClient http)
    {
        public HttpClient Http { get; } = http;
    }
}
