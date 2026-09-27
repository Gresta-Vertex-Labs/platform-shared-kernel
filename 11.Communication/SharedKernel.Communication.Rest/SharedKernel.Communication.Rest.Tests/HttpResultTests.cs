using System.Net;
using System.Text;
using SharedKernel.Communication.Internal;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Communication;

namespace SharedKernel.Communication.Rest.Tests;

public sealed class HttpResultTests
{
    private static readonly Dictionary<string, string?> NoRetries = new()
    {
        ["SharedKernel:Communication:Clients:inventory:Retry:MaxRetryAttempts"] = "0",
    };

    [Fact]
    public async Task A_body_is_read_with_source_generated_metadata()
    {
        using var harness = new RestHarness();
        harness.Stub.RespondJson(HttpMethod.Get, "/stock/sku-1", new StockLevel("sku-1", 3));

        Result<StockLevel> stock = await harness.Http.GetResultAsync("stock/sku-1", InventoryJson.Default.StockLevel);

        stock.Value.Should().Be(new StockLevel("sku-1", 3));
    }

    [Fact]
    public async Task A_body_is_read_with_web_defaults_by_reflection()
    {
        using var harness = new RestHarness();
        harness.Stub.Respond(HttpMethod.Get, "/stock/sku-1", (_, _) => Json("""{"sku":"sku-1","available":3}"""));

        Result<StockLevel> stock = await harness.Http.GetResultAsync<StockLevel>("stock/sku-1", CancellationToken.None);

        stock.Value.Should().Be(new StockLevel("sku-1", 3));
    }

    [Fact]
    public async Task A_platform_problem_becomes_the_services_error()
    {
        using var harness = new RestHarness();
        harness.Stub.RespondProblem(HttpMethod.Get, "/stock/sku-9", HttpStatusCode.NotFound, "inventory.sku_not_found", "No such SKU.");

        Result<StockLevel> stock = await harness.Http.GetResultAsync("stock/sku-9", InventoryJson.Default.StockLevel);

        stock.Error.Should().BeEquivalentTo(new { Type = ErrorType.NotFound, Code = "inventory.sku_not_found", Message = "No such SKU." });
    }

    [Fact]
    public async Task Field_errors_become_a_validation_error()
    {
        using var harness = new RestHarness();
        harness.Stub.RespondProblem(
            HttpMethod.Post,
            "/reservations",
            HttpStatusCode.BadRequest,
            "validation.failed",
            fieldErrors: new Dictionary<string, string[]> { ["quantity"] = ["Quantity must be positive."] });

        Result reserved = await harness.Http.PostResultAsync("reservations", new Reservation("sku-1", 0), InventoryJson.Default.Reservation);

        reserved.Error.Type.Should().Be(ErrorType.Validation);
        reserved.Error.Details.Should().ContainSingle().Which.Message.Should().Be("Quantity must be positive.");
    }

    [Fact]
    public async Task A_gateway_503_without_a_body_is_unavailable()
    {
        using var harness = new RestHarness(NoRetries);
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock/sku-1", HttpStatusCode.ServiceUnavailable);

        Result<StockLevel> stock = await harness.Http.GetResultAsync("stock/sku-1", InventoryJson.Default.StockLevel);

        stock.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unavailable, Code = "http.503" });
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "")]
    [InlineData(HttpStatusCode.NoContent, null)]
    [InlineData(HttpStatusCode.OK, "null")]
    public async Task A_missing_body_is_an_empty_body_error(HttpStatusCode status, string? body)
    {
        using var harness = new RestHarness();
        harness.Stub.Respond(HttpMethod.Get, "/stock/sku-1", (_, _) => body is null ? new HttpResponseMessage(status) : Json(body, status));

        Result<StockLevel> stock = await harness.Http.GetResultAsync("stock/sku-1", InventoryJson.Default.StockLevel);

        stock.Error.Code.Should().Be(CommunicationErrorCodes.EmptyBody);
    }

    [Fact]
    public async Task A_body_that_is_not_the_type_is_an_invalid_body_error()
    {
        using var harness = new RestHarness();
        harness.Stub.Respond(HttpMethod.Get, "/stock/sku-1", (_, _) => Json("<html>oops</html>"));

        Result<StockLevel> stock = await harness.Http.GetResultAsync("stock/sku-1", InventoryJson.Default.StockLevel);

        stock.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unexpected, Code = CommunicationErrorCodes.InvalidBody });
    }

    [Fact]
    public async Task An_unreachable_service_is_an_unavailable_error()
    {
        using var harness = new RestHarness(NoRetries);
        harness.Stub.Throw(HttpMethod.Get, "/stock/sku-1", new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        Result<StockLevel> stock = await harness.Http.GetResultAsync("stock/sku-1", InventoryJson.Default.StockLevel);

        stock.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unavailable, Code = CommunicationErrorCodes.Unreachable });
    }

    [Fact]
    public async Task No_access_token_is_its_own_error()
    {
        var tokenEndpoint = new StubHttpMessageHandler()
            .RespondJson(HttpMethod.Post, "/token", new { error = "invalid_client" }, HttpStatusCode.BadRequest);
        using var harness = new RestHarness(new Dictionary<string, string?>
        {
            ["SharedKernel:Communication:Clients:inventory:Retry:MaxRetryAttempts"] = "0",
            ["SharedKernel:Communication:Clients:inventory:Authentication:Mode"] = "ClientCredentials",
            ["SharedKernel:Communication:Clients:inventory:Authentication:ClientCredentials:TokenEndpoint"] = "https://login.example.com/token",
            ["SharedKernel:Communication:Clients:inventory:Authentication:ClientCredentials:ClientId"] = "checkout",
            ["SharedKernel:Communication:Clients:inventory:Authentication:ClientCredentials:ClientSecret"] = "secret",
        },
            services: s => s.UseStubHttpMessageHandler(ClientCredentialsTokenClient.HttpClientName, tokenEndpoint));

        Result result = await harness.Http.DeleteResultAsync("reservations/1");

        result.Error.Code.Should().Be(CommunicationErrorCodes.AccessTokenUnavailable);
        harness.Stub.Requests.Should().BeEmpty("the request was never sent");
        tokenEndpoint.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task The_callers_cancellation_is_thrown()
    {
        using var harness = new RestHarness();
        harness.Stub.RespondAsync(HttpMethod.Get, "/stock/sku-1", async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Func<Task> call = () => harness.Http.GetResultAsync("stock/sku-1", InventoryJson.Default.StockLevel, cancellation.Token);

        await call.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_POST_sends_camel_case_JSON_and_reads_the_answer()
    {
        using var harness = new RestHarness();
        harness.Stub.Respond(HttpMethod.Post, "/reservations", (_, _) => Json("""{"sku":"sku-1","quantity":2}""", HttpStatusCode.Created));

        Result<Reservation> reserved = await harness.Http.PostResultAsync(
            "reservations",
            new Reservation("sku-1", 2),
            InventoryJson.Default.Reservation,
            InventoryJson.Default.Reservation);

        reserved.Value.Should().Be(new Reservation("sku-1", 2));
        harness.Stub.Requests.Single().Body.Should().Be("""{"sku":"sku-1","quantity":2}""");
    }

    [Fact]
    public async Task PUT_and_DELETE_return_the_outcome()
    {
        using var harness = new RestHarness();
        harness.Stub.RespondStatus(HttpMethod.Put, "/reservations/1", HttpStatusCode.NoContent);
        harness.Stub.RespondProblem(HttpMethod.Delete, "/reservations/1", HttpStatusCode.Conflict, "inventory.reservation_shipped");

        Result put = await harness.Http.PutResultAsync("reservations/1", new Reservation("sku-1", 1), InventoryJson.Default.Reservation);
        Result delete = await harness.Http.DeleteResultAsync("reservations/1");

        put.IsSuccess.Should().BeTrue();
        delete.Error.Should().BeEquivalentTo(new { Type = ErrorType.Conflict, Code = "inventory.reservation_shipped" });
    }

    [Fact]
    public async Task A_response_reads_as_a_result()
    {
        using var ok = new HttpResponseMessage(HttpStatusCode.Accepted);
        using var notFound = new HttpResponseMessage(HttpStatusCode.NotFound);

        (await ok.ToResultAsync()).IsSuccess.Should().BeTrue();
        (await notFound.ToResultAsync()).Error.Should().BeEquivalentTo(new { Type = ErrorType.NotFound, Code = "http.404" });
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
