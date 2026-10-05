using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Execution;

namespace SharedKernel.Communication.Rest.Tests;

public sealed class RestPipelineTests
{
    private static readonly TenantId Tenant = TenantId.Parse("7f0c0f0e-5c1a-4a8e-9a55-2b3c4d5e6f70");

    [Fact]
    public async Task The_caller_travels_on_every_request()
    {
        using var harness = new RestHarness();
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock", HttpStatusCode.OK);
        var caller = TestRequestContext.ForTenant(Tenant, "user-1");
        caller.CorrelationId = "3f2d1c0b-aaaa-4bbb-8ccc-000000000001";

        using (RequestContextScope.Begin(caller))
        {
            (await harness.Http.GetAsync("stock")).EnsureSuccessStatusCode();
        }

        var request = harness.Stub.Requests.Single();
        request.Header(WellKnownHeaders.CorrelationId).Should().Be(caller.CorrelationId);
        request.Header(WellKnownHeaders.TenantId).Should().Be(Tenant.ToString());
        request.Header(WellKnownHeaders.ActorId).Should().Be("user-1");
        request.Header(WellKnownHeaders.ActorKind).Should().Be(nameof(ActorKind.User));
    }

    [Fact]
    public async Task A_call_without_a_caller_starts_a_new_correlation_id_and_keeps_it_across_retries()
    {
        using var harness = new RestHarness();
        harness.Stub.Respond(HttpMethod.Get, "/stock", (_, n) => new HttpResponseMessage(n == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));

        (await harness.Http.GetAsync("stock")).EnsureSuccessStatusCode();

        var ids = harness.Stub.Requests.Select(r => r.Header(WellKnownHeaders.CorrelationId)).ToList();
        ids.Should().HaveCount(2).And.OnlyContain(id => id == ids[0]);
        Guid.TryParseExact(ids[0], "D", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_header_the_request_has_is_kept()
    {
        using var harness = new RestHarness();
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock", HttpStatusCode.OK);
        using var request = new HttpRequestMessage(HttpMethod.Get, "stock");
        request.Headers.Add(WellKnownHeaders.CorrelationId, "mine");

        await harness.Http.SendAsync(request);

        harness.Stub.Requests.Single().Header(WellKnownHeaders.CorrelationId).Should().Be("mine");
    }

    [Fact]
    public async Task The_host_is_resolved_through_service_discovery()
    {
        using var harness = new RestHarness(new Dictionary<string, string?> { ["Services:inventory:http:0"] = "http://localhost:5999" });
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock", HttpStatusCode.OK);

        (await harness.Http.GetAsync("stock")).EnsureSuccessStatusCode();

        harness.Stub.Requests.Single().Uri!.Authority.Should().Be("localhost:5999");
    }

    [Fact]
    public async Task A_failed_GET_is_retried()
    {
        using var harness = new RestHarness();
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock", HttpStatusCode.ServiceUnavailable);

        await harness.Http.GetAsync("stock");

        harness.Stub.Requests.Should().HaveCount(4, "one attempt and three retries");
    }

    [Fact]
    public async Task A_failed_POST_is_not_retried_without_an_idempotency_key()
    {
        using var harness = new RestHarness();
        harness.Stub.RespondStatus(HttpMethod.Post, "/reservations", HttpStatusCode.ServiceUnavailable);

        await harness.Http.PostAsync("reservations", content: null);

        harness.Stub.Requests.Should().ContainSingle();
        harness.Stub.Requests.Single().Header(WellKnownHeaders.IdempotencyKey).Should().BeNull();
    }

    [Fact]
    public async Task With_idempotency_keys_a_POST_carries_one_key_across_its_retries()
    {
        using var harness = new RestHarness(new Dictionary<string, string?> { ["SharedKernel:Communication:Clients:inventory:PropagateIdempotencyKey"] = "true" });
        harness.Stub.Respond(HttpMethod.Post, "/reservations", (_, n) => new HttpResponseMessage(n < 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Created));

        (await harness.Http.PostAsync("reservations", content: null)).StatusCode.Should().Be(HttpStatusCode.Created);
        await harness.Http.PostAsync("reservations", content: null);

        var keys = harness.Stub.Requests.Select(r => r.Header(WellKnownHeaders.IdempotencyKey)).ToList();
        keys.Should().HaveCount(4);
        keys.Take(3).Should().OnlyContain(k => k == keys[0], "the retries of one call repeat its key");
        keys[3].Should().NotBe(keys[0], "a new call gets a new key");
    }

    [Fact]
    public async Task A_GET_gets_no_idempotency_key_and_a_callers_key_is_kept()
    {
        using var harness = new RestHarness(new Dictionary<string, string?> { ["SharedKernel:Communication:Clients:inventory:PropagateIdempotencyKey"] = "true" });
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock", HttpStatusCode.OK);
        harness.Stub.RespondStatus(HttpMethod.Post, "/reservations", HttpStatusCode.Created);
        using var post = new HttpRequestMessage(HttpMethod.Post, "reservations");
        post.Headers.Add(WellKnownHeaders.IdempotencyKey, "order-42");

        await harness.Http.GetAsync("stock");
        await harness.Http.SendAsync(post);

        harness.Stub.Requests.Select(r => r.Header(WellKnownHeaders.IdempotencyKey)).Should().Equal(null, "order-42");
    }

    [Fact]
    public async Task Retrying_a_POST_can_be_allowed_without_a_key()
    {
        using var harness = new RestHarness(new Dictionary<string, string?> { ["SharedKernel:Communication:Clients:inventory:Retry:RetryNonIdempotentMethods"] = "true" });
        harness.Stub.RespondStatus(HttpMethod.Post, "/reservations", HttpStatusCode.ServiceUnavailable);

        await harness.Http.PostAsync("reservations", content: null);

        harness.Stub.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task Zero_retries_means_one_attempt()
    {
        using var harness = new RestHarness(new Dictionary<string, string?> { ["SharedKernel:Communication:Clients:inventory:Retry:MaxRetryAttempts"] = "0" });
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock", HttpStatusCode.ServiceUnavailable);

        await harness.Http.GetAsync("stock");

        harness.Stub.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task The_circuit_opens_after_the_configured_failures()
    {
        using var harness = new RestHarness(new Dictionary<string, string?>
        {
            ["SharedKernel:Communication:Clients:inventory:Retry:MaxRetryAttempts"] = "0",
            ["SharedKernel:Communication:Clients:inventory:CircuitBreaker:MinimumThroughput"] = "2",
            ["SharedKernel:Communication:Clients:inventory:CircuitBreaker:FailureRatio"] = "0.5",
        });
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock", HttpStatusCode.InternalServerError);

        await harness.Http.GetAsync("stock");
        await harness.Http.GetAsync("stock");
        Result third = await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Get, "stock"));

        third.Error.Code.Should().Be(CommunicationErrorCodes.CircuitOpen);
        harness.Stub.Requests.Should().HaveCount(2, "the open circuit refused the third call without sending it");
    }

    [Fact]
    public async Task A_disabled_circuit_never_opens()
    {
        using var harness = new RestHarness(new Dictionary<string, string?>
        {
            ["SharedKernel:Communication:Clients:inventory:Retry:MaxRetryAttempts"] = "0",
            ["SharedKernel:Communication:Clients:inventory:CircuitBreaker:Enabled"] = "false",
            ["SharedKernel:Communication:Clients:inventory:CircuitBreaker:MinimumThroughput"] = "2",
        });
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock", HttpStatusCode.InternalServerError);

        for (var i = 0; i < 6; i++)
        {
            await harness.Http.GetAsync("stock");
        }

        harness.Stub.Requests.Should().HaveCount(6);
    }

    [Fact]
    public async Task A_slow_attempt_times_out()
    {
        using var harness = new RestHarness(new Dictionary<string, string?>
        {
            ["SharedKernel:Communication:Clients:inventory:Retry:MaxRetryAttempts"] = "0",
            ["SharedKernel:Communication:Clients:inventory:AttemptTimeout"] = "00:00:00.200",
            ["SharedKernel:Communication:Clients:inventory:CircuitBreaker:Enabled"] = "false",
        });
        harness.Stub.RespondAsync(HttpMethod.Get, "/stock", async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Result result = await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Get, "stock"));

        result.Error.Code.Should().Be(CommunicationErrorCodes.Timeout);
        result.Error.Type.Should().Be(Primitives.Errors.ErrorType.Timeout);
    }

    [Fact]
    public async Task Hedging_races_a_slow_GET_but_never_a_POST()
    {
        using var harness = new RestHarness(
            new Dictionary<string, string?> { ["SharedKernel:Communication:Clients:inventory:Hedging:Delay"] = "00:00:00.050" },
            configure: c => c.UseHedging());
        harness.Stub.RespondAsync(HttpMethod.Get, "/stock", async (_, n, ct) =>
        {
            if (n == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        harness.Stub.RespondAsync(HttpMethod.Post, "/reservations", async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        (await harness.Http.GetAsync("stock")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await harness.Http.PostAsync("reservations", content: null)).StatusCode.Should().Be(HttpStatusCode.Created);

        harness.Stub.Requests.Count(r => r.Method == HttpMethod.Get).Should().Be(2);
        harness.Stub.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1);
    }

    [Fact]
    public async Task An_API_key_from_configuration_is_sent()
    {
        using var harness = new RestHarness(new Dictionary<string, string?>
        {
            ["SharedKernel:Communication:Clients:inventory:Authentication:Mode"] = "ApiKey",
            ["SharedKernel:Communication:Clients:inventory:Authentication:ApiKey:Value"] = "k-123",
        });
        harness.Stub.RespondStatus(HttpMethod.Get, "/stock", HttpStatusCode.OK);

        await harness.Http.GetAsync("stock");

        harness.Stub.Requests.Single().Header("X-Api-Key").Should().Be("k-123");
    }

    [Fact]
    public async Task A_handler_added_through_the_builder_runs_once_per_call()
    {
        var counter = new CountingHandler();
        using var harness = new RestHarness(configure: c => c.HttpClientBuilder.AddHttpMessageHandler(() => counter));
        harness.Stub.Respond(HttpMethod.Get, "/stock", (_, n) => new HttpResponseMessage(n == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));

        await harness.Http.GetAsync("stock");

        counter.Calls.Should().Be(1);
        harness.Stub.Requests.Should().HaveCount(2);
    }

    private sealed class CountingHandler : DelegatingHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
