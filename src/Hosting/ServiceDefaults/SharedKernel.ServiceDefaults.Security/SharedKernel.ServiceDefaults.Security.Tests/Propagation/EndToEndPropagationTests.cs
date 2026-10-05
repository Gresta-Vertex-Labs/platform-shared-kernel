using System.Diagnostics;
using FluentAssertions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.ServiceDefaults.Security.Tests.Propagation;

/// <summary>
/// P-566: the caller's correlation id, tenant and actor — and the idempotency key — survive every hop, end to end,
/// through the platform's real inbound and outbound adapters: HTTP → REST, HTTP → gRPC, HTTP → bus → consumer → REST
/// and job → REST.
/// </summary>
/// <remarks>
/// Everything runs in process: the services are ASP.NET Core hosts on <c>TestServer</c> and the bus is MassTransit's
/// in-memory transport, so no Docker is needed. Each hop is asserted twice at the downstream service: the header it
/// received, and the correlation id its own inbound adapter then made ambient — the value an onward call would carry.
/// </remarks>
public sealed class EndToEndPropagationTests
{
    private static readonly TenantId Tenant = new(Guid.Parse("7a1c0e4e-5b8d-4c1f-9e2a-3d6f8b0c1a2e"));

    private static async Task<(DownstreamService Downstream, FrontService Front)> StartAsync()
    {
        var downstream = await DownstreamService.StartAsync();
        var front = await FrontService.StartAsync(downstream, Tenant);
        return (downstream, front);
    }

    private static HttpRequestMessage Get(string path, string correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(WellKnownHeaders.CorrelationId, correlationId);
        return request;
    }

    /// <summary>
    /// HTTP → REST. Also defect 1: the idempotency key the REST client sends arrives under the header the inbound
    /// <c>GetIdempotencyKey()</c> reads.
    /// </summary>
    [Fact]
    public async Task HttpToRest_CorrelationTenantActorAndIdempotencyKeySurvive()
    {
        var (downstream, front) = await StartAsync();
        await using var _ = downstream;
        await using var __ = front;
        using var activity = new Activity("inbound").Start();

        using var response = await front.CreateClient().SendAsync(Get("/rest", "e2e-http-rest-1"));

        response.IsSuccessStatusCode.Should().BeTrue();
        response.Headers.GetValues(WellKnownHeaders.CorrelationId).Should().Equal("e2e-http-rest-1");

        var call = await downstream.Recorder.WaitForAsync("http-rest");
        call.CorrelationHeader.Should().Be("e2e-http-rest-1", "the caller's id is forwarded, never Activity.Id (defect 4)");
        call.AmbientCorrelationId.Should().Be("e2e-http-rest-1");
        call.TenantHeader.Should().Be(Tenant.ToString());
        call.ActorKindHeader.Should().Be("User");
        call.ActorIdHeader.Should().Be(FrontService.UserId);
        call.IdempotencyKey.Should().NotBeNullOrWhiteSpace(
            "the outbound Idempotency-Key must arrive under the name the inbound endpoint reads (defect 1)");
    }

    [Fact]
    public async Task HttpToGrpc_CorrelationAndTenantSurvive()
    {
        var (downstream, front) = await StartAsync();
        await using var _ = downstream;
        await using var __ = front;

        using var response = await front.CreateClient().SendAsync(Get("/grpc", "e2e-http-grpc-1"));

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var call = await downstream.Recorder.WaitForAsync("http-grpc");
        call.CorrelationHeader.Should().Be("e2e-http-grpc-1");
        call.AmbientCorrelationId.Should().Be("e2e-http-grpc-1", "the gRPC server restores the caller's id");
        call.TenantHeader.Should().Be(Tenant.ToString());
        call.ActorKindHeader.Should().Be("User");
    }

    /// <summary>
    /// HTTP → bus → consumer → REST. Defect 3: an outbound call made from inside a consumer carries the publisher's
    /// tenant, because the consume filter makes the rebuilt caller ambient.
    /// </summary>
    [Fact]
    public async Task HttpToBusToConsumerToRest_CorrelationAndTenantSurvive()
    {
        var (downstream, front) = await StartAsync();
        await using var _ = downstream;
        await using var __ = front;

        using var response = await front.CreateClient().SendAsync(Get("/bus", "e2e-http-bus-1"));

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        var call = await downstream.Recorder.WaitForAsync("http-bus-consumer-rest");
        call.CorrelationHeader.Should().Be("e2e-http-bus-1", "the correlation id must survive the bus hop unchanged");
        call.AmbientCorrelationId.Should().Be("e2e-http-bus-1");
        call.TenantHeader.Should().Be(Tenant.ToString(), "a consumer's outbound call carries the publisher's tenant (defect 3)");
        call.ActorIdHeader.Should().Be(FrontService.UserId, "the consumer's work stays attributed to the original caller");
    }

    [Fact]
    public async Task JobToRest_TheJobsTenantAndCorrelationIdSurvive()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using var downstream = await DownstreamService.StartAsync();
        await using var jobs = await JobService.StartAsync(downstream, Tenant, start);

        // Advance the fake clock a second at a time until the job fires: the scheduler takes its baseline when its
        // loop first runs, which may be after the host reports started.
        var callTask = downstream.Recorder.WaitForAsync("job");
        for (var second = 1; !callTask.IsCompleted && second <= 60; second++)
        {
            jobs.Clock.Set(start.AddSeconds(second));
            await Task.WhenAny(callTask, Task.Delay(200));
        }

        var call = await callTask;
        call.TenantHeader.Should().Be(Tenant.ToString(), "the job runs for its configured TenantScope");
        call.ActorKindHeader.Should().Be("System");
        call.ActorIdHeader.Should().Be(JobService.JobName);
        Guid.TryParseExact(call.CorrelationHeader!, "D", out _).Should().BeTrue("each job run starts a new correlation id");
        call.AmbientCorrelationId.Should().Be(call.CorrelationHeader, "the downstream service continues the job's id");
    }
}
