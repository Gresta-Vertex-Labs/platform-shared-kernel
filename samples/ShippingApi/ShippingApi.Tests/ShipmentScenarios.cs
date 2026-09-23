using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharedKernel.Application.Context;

namespace ShippingApi.Tests;

/// <summary>
/// The messaging packages exercised end to end through the sample's HTTP surface and a real
/// RabbitMQ broker: nothing here is in-memory, and a publish genuinely leaves the process.
/// </summary>
[Collection(SampleHostFixture.Name)]
public sealed class ShipmentScenarios
{
    private readonly SampleHost _host;

    /// <summary>Initialises the scenarios against the shared host and broker.</summary>
    /// <param name="host">The running sample.</param>
    public ShipmentScenarios(SampleHost host)
    {
        _host = host;
    }

    // -----------------------------------------------------------------------------------------
    // Publish -> broker -> consume
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The baseline round trip: <c>IEventPublisher</c> wraps the event in a CloudEvents envelope,
    /// RabbitMQ carries it, and <c>ConsumerBase</c> unwraps it into the read model.
    /// </summary>
    [Fact]
    public async Task DispatchedEvent_TravelsThroughTheBroker_AndReachesItsConsumer()
    {
        HttpClient api = _host.Api(Guid.NewGuid(), "dispatcher-1");

        Guid shipmentId = await SampleHost.DispatchAsync(api, "acme-freight", "TRK-100");

        ShipmentView? view = await ReadAsync(api, shipmentId);

        view.Should().NotBeNull();
        view!.Carrier.Should().Be("acme-freight");
        view.TrackingNumber.Should().Be("TRK-100");
    }

    /// <summary>
    /// The read endpoint is honest about asynchrony: a shipment nobody dispatched is a 404, so a
    /// passing round-trip test cannot be passing on a stub. Like every error of the API, it is an
    /// RFC 9457 problem carrying its error code.
    /// </summary>
    [Fact]
    public async Task UnknownShipment_Is404()
    {
        HttpClient api = _host.Api();

        HttpResponseMessage response = await api.GetAsync($"/shipments/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(SampleHost.Json);
        problem.GetProperty("errorCode").GetString().Should().Be("shipment.not_found");
    }

    // -----------------------------------------------------------------------------------------
    // Caller identity across the bus (P-561)
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The publisher's tenant and actor reach the consumer, which read them from
    /// <c>IRequestContext</c> — never from the message body, which does not carry them.
    /// </summary>
    [Fact]
    public async Task PublisherTenantAndActor_ReachTheConsumer()
    {
        var tenantId = Guid.NewGuid();
        HttpClient api = _host.Api(tenantId, "operator-7");

        Guid shipmentId = await SampleHost.DispatchAsync(api);
        ShipmentView? view = await ReadAsync(api, shipmentId);

        view.Should().NotBeNull();
        view!.TenantId.Should().Be(tenantId,
            "a consumer with no tenant cannot write tenant-scoped rows — this is what makes it able to");
        view.ActorId.Should().Be("operator-7");
        view.ActorKind.Should().Be(ActorKind.User);
    }

    /// <summary>
    /// Two tenants publishing concurrently do not see each other's identity: the context is
    /// per-delivery, not per-process.
    /// </summary>
    [Fact]
    public async Task TwoTenants_EachConsumerSeesItsOwnPublisher()
    {
        var acme = Guid.NewGuid();
        var globex = Guid.NewGuid();

        HttpClient acmeApi = _host.Api(acme, "acme-operator");
        HttpClient globexApi = _host.Api(globex, "globex-operator");

        Guid[] shipmentIds = await Task.WhenAll(
            SampleHost.DispatchAsync(acmeApi, "acme-freight", "TRK-ACME"),
            SampleHost.DispatchAsync(globexApi, "globex-logistics", "TRK-GLOBEX"));

        ShipmentView? acmeView = await ReadAsync(acmeApi, shipmentIds[0]);
        ShipmentView? globexView = await ReadAsync(globexApi, shipmentIds[1]);

        acmeView!.TenantId.Should().Be(acme);
        acmeView.ActorId.Should().Be("acme-operator");
        globexView!.TenantId.Should().Be(globex);
        globexView.ActorId.Should().Be("globex-operator");
    }

    /// <summary>
    /// An unauthenticated publisher produces a consumer with no tenant and no actor, rather than
    /// one that silently inherits the consuming service's own identity.
    /// </summary>
    [Fact]
    public async Task AnonymousPublisher_LeavesTheConsumerWithNoTenant()
    {
        HttpClient api = _host.Api();

        Guid shipmentId = await SampleHost.DispatchAsync(api);
        ShipmentView? view = await ReadAsync(api, shipmentId);

        view.Should().NotBeNull();
        view!.TenantId.Should().BeNull("persistence must keep failing closed");
        view.ActorId.Should().BeNull();
        view.ActorKind.Should().Be(ActorKind.Anonymous);
    }

    // -----------------------------------------------------------------------------------------
    // Send: one endpoint, not a broadcast
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <c>IMessageBus.SendAsync</c> addresses the queue the route map names, and the hold consumer
    /// there updates the shipment.
    /// </summary>
    [Fact]
    public async Task HoldCommand_IsRoutedToItsQueue_AndConsumed()
    {
        HttpClient api = _host.Api(Guid.NewGuid(), "operator-7");

        Guid shipmentId = await SampleHost.DispatchAsync(api);
        (await ReadAsync(api, shipmentId)).Should().NotBeNull("the shipment must exist before it can be held");

        HttpResponseMessage held = await api.PostAsJsonAsync(
            $"/shipments/{shipmentId}/hold",
            new HoldRequest("customs"));

        held.StatusCode.Should().Be(HttpStatusCode.Accepted);

        ShipmentView? view = await SampleHost.EventuallyAsync(async () =>
        {
            ShipmentView? current = await ReadAsync(api, shipmentId);
            return current?.HoldReason is null ? null : current;
        });

        view.Should().NotBeNull();
        view!.HoldReason.Should().Be("customs");
    }

    // -----------------------------------------------------------------------------------------
    // Delayed delivery: held by the broker, not by this process
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A scheduled message is delivered after its delay through the broker's delayed-message
    /// exchange. Reaching the consumer at all is the proof that no in-process timer was involved.
    /// </summary>
    [Fact]
    public async Task ScheduledReminder_IsDeliveredAfterItsDelay()
    {
        HttpClient api = _host.Api(Guid.NewGuid(), "operator-7");

        Guid shipmentId = await SampleHost.DispatchAsync(api);
        (await ReadAsync(api, shipmentId)).Should().NotBeNull();

        HttpResponseMessage scheduled = await api.PostAsJsonAsync(
            $"/shipments/{shipmentId}/chase",
            new ChaseRequest(DelayMilliseconds: 500));

        scheduled.StatusCode.Should().Be(HttpStatusCode.Accepted);

        ShipmentView? view = await SampleHost.EventuallyAsync(async () =>
        {
            ShipmentView? current = await ReadAsync(api, shipmentId);
            return current?.HoldReason == "chased" ? current : null;
        });

        view.Should().NotBeNull("the broker must deliver the reminder once its delay elapses");
    }

    // -----------------------------------------------------------------------------------------
    // Failure: retry, then a fault the service can see
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A consumer that keeps throwing exhausts its retries and produces a fault the fault consumer
    /// observes, rather than a message that disappears.
    /// </summary>
    [Fact]
    public async Task FailingConsumer_ExhaustsRetries_ThenTheFaultIsObserved()
    {
        HttpClient api = _host.Api(Guid.NewGuid(), "operator-7");
        var shipmentId = Guid.CreateVersion7();

        HttpResponseMessage accepted = await api.PostAsync($"/shipments/{shipmentId}/check", content: null);
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);

        FaultResponse? fault = await SampleHost.EventuallyAsync(async () =>
        {
            HttpResponseMessage response = await api.GetAsync($"/shipments/{shipmentId}/fault");
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<FaultResponse>(SampleHost.Json)
                : null;
        });

        fault.Should().NotBeNull("the fault consumer runs once the retry policy is exhausted");
        fault!.Message.Should().Contain(shipmentId.ToString(),
            "the fault carries the original exception, not a generic one");
    }

    // -----------------------------------------------------------------------------------------
    // Readiness
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The bus-backed readiness probe reports healthy while connected, so Kubernetes does not send
    /// traffic to a replica whose broker connection is down.
    /// </summary>
    [Fact]
    public async Task ReadinessEndpoint_ReportsHealthy_WhileTheBusIsConnected()
    {
        HttpClient api = _host.Api();

        HttpResponseMessage live = await api.GetAsync("/health/live");
        HttpResponseMessage ready = await api.GetAsync("/health/ready");

        (await live.Content.ReadAsStringAsync()).Should().Contain("Healthy");
        (await ready.Content.ReadAsStringAsync()).Should().Contain("Healthy");
    }

    private async Task<ShipmentView?> ReadAsync(HttpClient api, Guid shipmentId) =>
        await SampleHost.EventuallyAsync(async () =>
        {
            HttpResponseMessage response = await api.GetAsync($"/shipments/{shipmentId}");
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<ShipmentView>(SampleHost.Json)
                : null;
        });

    /// <summary>The shape <c>GET /shipments/{id}/fault</c> returns.</summary>
    /// <param name="Message">The exception message the consumer failed with.</param>
    private sealed record FaultResponse(string Message);
}
