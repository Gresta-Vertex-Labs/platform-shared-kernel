using System.Net;
using SharedKernel.Communication.Grpc.Tests.Probe;
using SharedKernel.Communication.Internal;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Communication;
using SharedKernel.Testing.Execution;

namespace SharedKernel.Communication.Grpc.Tests;

public sealed class GrpcClientTests
{
    private static readonly TenantId Tenant = TenantId.Parse("7f0c0f0e-5c1a-4a8e-9a55-2b3c4d5e6f70");

    [Fact]
    public async Task The_caller_travels_as_metadata()
    {
        await using var harness = await GrpcHarness.StartAsync();
        var caller = TestRequestContext.ForTenant(Tenant, "user-1");
        caller.CorrelationId = "3f2d1c0b-aaaa-4bbb-8ccc-000000000002";

        EchoReply reply;
        using (RequestContextScope.Begin(caller))
        {
            reply = await harness.Client.EchoAsync(new EchoRequest { Text = Unique("caller") });
        }

        reply.Metadata.Should().Contain(WellKnownHeaders.CorrelationId.ToLowerInvariant(), caller.CorrelationId)
            .And.Contain(WellKnownHeaders.TenantId.ToLowerInvariant(), Tenant.ToString())
            .And.Contain(WellKnownHeaders.ActorId.ToLowerInvariant(), "user-1")
            .And.Contain(WellKnownHeaders.ActorKind.ToLowerInvariant(), nameof(ActorKind.User));
    }

    [Fact]
    public async Task Metadata_the_call_carries_is_kept()
    {
        await using var harness = await GrpcHarness.StartAsync();

        EchoReply reply = await harness.Client.EchoAsync(
            new EchoRequest { Text = Unique("mine") },
            new Metadata { { WellKnownHeaders.CorrelationId, "mine" } });

        reply.Metadata[WellKnownHeaders.CorrelationId.ToLowerInvariant()].Should().Be("mine");
    }

    [Fact]
    public async Task An_unavailable_call_is_retried_with_the_same_correlation_id()
    {
        await using var harness = await GrpcHarness.StartAsync();

        EchoReply reply = await harness.Client.EchoAsync(new EchoRequest { Text = Unique("retry"), FailAttempts = 2 });

        reply.Attempt.Should().Be(3);
        Guid.TryParseExact(reply.Metadata[WellKnownHeaders.CorrelationId.ToLowerInvariant()], "D", out _).Should().BeTrue();
    }

    [Fact]
    public async Task One_attempt_means_no_retry()
    {
        await using var harness = await GrpcHarness.StartAsync(new Dictionary<string, string?>
        {
            ["SharedKernel:Communication:Clients:probe:Retry:MaxAttempts"] = "1",
        });

        Result<EchoReply> reply = await harness.Client.EchoAsync(new EchoRequest { Text = Unique("no-retry"), FailAttempts = 1 }).ToResultAsync();

        reply.Error.Code.Should().Be("grpc.unavailable");
    }

    [Fact]
    public async Task Every_call_gets_the_configured_deadline_unless_it_sets_its_own()
    {
        await using var harness = await GrpcHarness.StartAsync(new Dictionary<string, string?>
        {
            ["SharedKernel:Communication:Clients:probe:Deadline"] = "00:00:05",
        });

        EchoReply configured = await harness.Client.EchoAsync(new EchoRequest { Text = Unique("deadline") });
        EchoReply own = await harness.Client.EchoAsync(new EchoRequest { Text = Unique("own") }, deadline: DateTime.UtcNow.AddSeconds(60));

        configured.DeadlineSeconds.Should().BeInRange(3, 5);
        own.DeadlineSeconds.Should().BeInRange(55, 60);
    }

    [Fact]
    public async Task A_platform_status_becomes_the_services_error()
    {
        await using var harness = await GrpcHarness.StartAsync();

        Result<EchoReply> reply = await harness.Client.EchoAsync(new EchoRequest { Text = "not-found" }).ToResultAsync();

        reply.Error.Should().BeEquivalentTo(new { Type = ErrorType.NotFound, Code = "inventory.sku_not_found", Message = "No such SKU." });
    }

    [Fact]
    public async Task Field_violations_become_a_validation_error_with_their_field()
    {
        await using var harness = await GrpcHarness.StartAsync();

        Result<EchoReply> reply = await harness.Client.EchoAsync(new EchoRequest { Text = "invalid" }).ToResultAsync();

        reply.Error.Type.Should().Be(ErrorType.Validation);
        Error field = reply.Error.Details.Should().ContainSingle().Subject;
        field.Code.Should().Be("quantity.positive");
        field.Message.Should().Be("Quantity must be positive.");
        field.MessageArguments![ErrorArgumentNames.PropertyPath].Should().Be("quantity");
    }

    [Fact]
    public async Task A_bare_status_gets_the_grpc_code_of_its_status()
    {
        await using var harness = await GrpcHarness.StartAsync();

        Result<EchoReply> reply = await harness.Client.EchoAsync(new EchoRequest { Text = "bare" }).ToResultAsync();

        reply.Error.Should().BeEquivalentTo(new { Type = ErrorType.BusinessRule, Code = "grpc.failed_precondition", Message = "Not now." });
    }

    [Fact]
    public async Task A_call_past_its_deadline_is_a_timeout()
    {
        await using var harness = await GrpcHarness.StartAsync(new Dictionary<string, string?>
        {
            ["SharedKernel:Communication:Clients:probe:Deadline"] = "00:00:00.300",
        });

        Result<EchoReply> reply = await harness.Client.EchoAsync(new EchoRequest { Text = "slow" }).ToResultAsync();

        reply.Error.Should().BeEquivalentTo(new { Type = ErrorType.Timeout, Code = CommunicationErrorCodes.Timeout });
    }

    [Fact]
    public async Task An_unreachable_service_is_unreachable()
    {
        await using var harness = await GrpcHarness.StartAsync(
            new Dictionary<string, string?> { ["SharedKernel:Communication:Clients:probe:Retry:MaxAttempts"] = "1" },
            connection: () => new StubHttpMessageHandler().Throw(HttpMethod.Post, "/sharedkernel.communication.tests.Probe/Echo", new HttpRequestException(HttpRequestError.ConnectionError, "refused", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused))));

        Result<EchoReply> reply = await harness.Client.EchoAsync(new EchoRequest { Text = "x" }).ToResultAsync();

        reply.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unavailable, Code = CommunicationErrorCodes.Unreachable });
    }

    [Fact]
    public async Task Calls_are_spread_round_robin_across_the_services_endpoints()
    {
        var connection = new StubHttpMessageHandler()
            .Throw(HttpMethod.Post, "/sharedkernel.communication.tests.Probe/Echo", new HttpRequestException("recorded"));
        await using var harness = await GrpcHarness.StartAsync(
            new Dictionary<string, string?>
            {
                ["SharedKernel:Communication:Clients:probe:Retry:MaxAttempts"] = "1",
                ["Services:probe:http:0"] = "http://10.0.0.1:5001",
                ["Services:probe:http:1"] = "http://10.0.0.2:5001",
            },
            connection: () => connection);

        for (var i = 0; i < 4; i++)
        {
            await harness.Client.EchoAsync(new EchoRequest { Text = "x" }).ToResultAsync();
        }

        var hosts = connection.Requests.Select(r => r.Uri!.Host).ToList();
        hosts.Should().HaveCount(4).And.Contain(["10.0.0.1", "10.0.0.2"]);
        hosts.Zip(hosts.Skip(1)).Should().OnlyContain(pair => pair.First != pair.Second, "each call goes to the next endpoint");
    }

    [Fact]
    public async Task An_API_key_reaches_the_service()
    {
        await using var harness = await GrpcHarness.StartAsync(new Dictionary<string, string?>
        {
            ["SharedKernel:Communication:Clients:probe:Authentication:Mode"] = "ApiKey",
            ["SharedKernel:Communication:Clients:probe:Authentication:ApiKey:Value"] = "k-123",
        });

        EchoReply reply = await harness.Client.EchoAsync(new EchoRequest { Text = Unique("key") });

        reply.Metadata["x-api-key"].Should().Be("k-123");
    }

    [Fact]
    public async Task No_access_token_is_its_own_error()
    {
        var tokenEndpoint = new StubHttpMessageHandler()
            .RespondJson(HttpMethod.Post, "/token", new { error = "invalid_client" }, HttpStatusCode.BadRequest);
        await using var harness = await GrpcHarness.StartAsync(
            new Dictionary<string, string?>
            {
                ["SharedKernel:Communication:Clients:probe:Retry:MaxAttempts"] = "1",
                ["SharedKernel:Communication:Clients:probe:Authentication:Mode"] = "ClientCredentials",
                ["SharedKernel:Communication:Clients:probe:Authentication:ClientCredentials:TokenEndpoint"] = "https://login.example.com/token",
                ["SharedKernel:Communication:Clients:probe:Authentication:ClientCredentials:ClientId"] = "checkout",
                ["SharedKernel:Communication:Clients:probe:Authentication:ClientCredentials:ClientSecret"] = "secret",
            },
            services: s => s.UseStubHttpMessageHandler(ClientCredentialsTokenClient.HttpClientName, tokenEndpoint));

        Result<EchoReply> reply = await harness.Client.EchoAsync(new EchoRequest { Text = "x" }).ToResultAsync();

        reply.Error.Code.Should().Be(CommunicationErrorCodes.AccessTokenUnavailable);
    }

    [Fact]
    public async Task The_callers_cancellation_is_thrown()
    {
        await using var harness = await GrpcHarness.StartAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Func<Task> call = () => harness.Client.EchoAsync(new EchoRequest { Text = "slow" }, cancellationToken: cancellation.Token).ToResultAsync(cancellation.Token);

        await call.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_faked_platform_failure_reads_back_as_the_same_error()
    {
        Error error = Error.Conflict("inventory.reservation_exists", "Already reserved.");

        Result<EchoReply> reply = await GrpcCalls.Failure<EchoReply>(error).ToResultAsync();
        Result<EchoReply> success = await GrpcCalls.Success(new EchoReply { Text = "ok" }).ToResultAsync();

        reply.Error.Should().BeEquivalentTo(new { Type = ErrorType.Conflict, error.Code, error.Message });
        success.Value.Text.Should().Be("ok");
    }

    [Theory]
    [InlineData("https+http://probe")]
    [InlineData("ftp://probe")]
    public async Task An_address_a_channel_cannot_use_fails_at_startup(string address)
    {
        Func<Task> start = () => GrpcHarness.StartAsync(new Dictionary<string, string?> { ["SharedKernel:Communication:Clients:probe:Address"] = address });

        await start.Should().ThrowAsync<Microsoft.Extensions.Options.OptionsValidationException>();
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
