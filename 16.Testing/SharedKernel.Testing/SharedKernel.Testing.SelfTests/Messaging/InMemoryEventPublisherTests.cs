using SharedKernel.Execution.Tenancy;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Testing.Messaging;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Messaging;

public sealed class InMemoryEventPublisherTests
{
    [IntegrationEvent("tests.testing.messaging.in-memory-event-publisher")]
    private sealed record TestIntegrationEvent(string Value) : IIntegrationEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();

        public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Test-double propagator that appends its <paramref name="name"/> to a shared,
    /// caller-supplied log every time <see cref="Propagate"/> runs — proves propagator
    /// EXECUTION ORDER independent of header-dictionary enumeration order — and writes a
    /// distinct, identifiable header so the resulting <see cref="PublishContext"/> can also
    /// be inspected directly. Mirrors <c>InMemoryMessageBusTests.OrderTrackingPropagator</c>.
    /// </summary>
    private sealed class OrderTrackingPropagator(string name, List<string> executionLog) : IMessageHeaderPropagator
    {
        public void Propagate(PublishContext context)
        {
            executionLog.Add(name);
            context.WithHeader($"x-propagator-{name}", name);
        }
    }

    /// <summary>Test-double propagator that writes one fixed header key/value pair — used to prove the
    /// explicit-configure-callback-wins-on-key-conflict precedence rule.</summary>
    private sealed class HeaderSettingPropagator(string key, string value) : IMessageHeaderPropagator
    {
        public void Propagate(PublishContext context) => context.WithHeader(key, value);
    }

    /// <summary>Test-double propagator that sets CorrelationId, CausationId, and a custom header in one
    /// call — used to prove all three round-trip through the captured <see cref="PublishContext"/>.</summary>
    private sealed class FullContextPropagator(Guid correlationId, Guid causationId, string headerKey, string headerValue)
        : IMessageHeaderPropagator
    {
        public void Propagate(PublishContext context) =>
            context.WithCorrelationId(correlationId).WithCausationId(causationId).WithHeader(headerKey, headerValue);
    }

    /// <summary>
    /// Test-double propagator shaped like the real, shipped 07.Messaging <c>TenantHeaderPropagator</c> —
    /// populates only <see cref="PublishContext.TenantId"/> via <see cref="PublishContext.WithTenantId"/>.
    /// Mirrors <c>InMemoryMessageBusTests.TenantPropagator</c> (T-74/P-352/WO-054).
    /// </summary>
    private sealed class TenantPropagator(TenantId tenantId) : IMessageHeaderPropagator
    {
        public void Propagate(PublishContext context) => context.WithTenantId(tenantId);
    }

    /// <summary>Test-double propagator that populates only <see cref="PublishContext.PartitionKey"/> via
    /// <see cref="PublishContext.WithPartitionKey"/>. Mirrors
    /// <c>InMemoryMessageBusTests.PartitionKeyPropagator</c> (T-74/P-352/WO-054).</summary>
    private sealed class PartitionKeyPropagator(string partitionKey) : IMessageHeaderPropagator
    {
        public void Propagate(PublishContext context) => context.WithPartitionKey(partitionKey);
    }

    [Fact]
    public async Task PublishAsync_RecordsEvent_InPublishedList()
    {
        var publisher = new InMemoryEventPublisher();
        var evt = new TestIntegrationEvent("a");

        await publisher.PublishAsync(evt, CancellationToken.None);

        Assert.Contains(evt, publisher.Published);
    }

    [Fact]
    public async Task PublishedOf_FiltersToRequestedType_InPublishOrder()
    {
        var publisher = new InMemoryEventPublisher();
        await publisher.PublishAsync(new TestIntegrationEvent("a"), CancellationToken.None);
        await publisher.PublishAsync(new TestIntegrationEvent("b"), CancellationToken.None);

        var results = publisher.PublishedOf<TestIntegrationEvent>();

        Assert.Equal(["a", "b"], results.Select(r => r.Value));
    }

    [Fact]
    public async Task ShouldHavePublished_Match_ReturnsFirst()
    {
        var publisher = new InMemoryEventPublisher();
        await publisher.PublishAsync(new TestIntegrationEvent("a"), CancellationToken.None);

        var found = publisher.ShouldHavePublished<TestIntegrationEvent>();
        Assert.Equal("a", found.Value);
    }

    [Fact]
    public void ShouldHavePublished_NoMatch_Throws()
    {
        var publisher = new InMemoryEventPublisher();
        Assert.Throws<InvalidOperationException>(publisher.ShouldHavePublished<TestIntegrationEvent>);
    }

    [Fact]
    public void ShouldHavePublished_NoMatch_MessageNamesTheWireNameAndVersion()
    {
        var publisher = new InMemoryEventPublisher();

        var ex = Assert.Throws<InvalidOperationException>(publisher.ShouldHavePublished<TestIntegrationEvent>);

        Assert.Contains("'TestIntegrationEvent' (tests.testing.messaging.in-memory-event-publisher v1)", ex.Message);
    }

    [Fact]
    public void ShouldHavePublished_NoMatchForAnInterface_MessageFallsBackToTheTypeName()
    {
        var publisher = new InMemoryEventPublisher();

        var ex = Assert.Throws<InvalidOperationException>(publisher.ShouldHavePublished<IIntegrationEvent>);

        Assert.Contains("'IIntegrationEvent' but none", ex.Message);
    }

    [Fact]
    public async Task ShouldHavePublishedOnce_ExactlyOne_ReturnsIt()
    {
        var publisher = new InMemoryEventPublisher();
        await publisher.PublishAsync(new TestIntegrationEvent("a"), CancellationToken.None);

        publisher.ShouldHavePublishedOnce<TestIntegrationEvent>();
    }

    [Fact]
    public async Task ShouldHavePublishedOnce_MoreThanOne_Throws()
    {
        var publisher = new InMemoryEventPublisher();
        await publisher.PublishAsync(new TestIntegrationEvent("a"), CancellationToken.None);
        await publisher.PublishAsync(new TestIntegrationEvent("b"), CancellationToken.None);

        Assert.Throws<InvalidOperationException>(publisher.ShouldHavePublishedOnce<TestIntegrationEvent>);
    }

    [Fact]
    public void ShouldNotHavePublished_Empty_DoesNotThrow()
    {
        var publisher = new InMemoryEventPublisher();
        publisher.ShouldNotHavePublished<TestIntegrationEvent>();
    }

    [Fact]
    public async Task ShouldNotHavePublished_HasMatch_Throws()
    {
        var publisher = new InMemoryEventPublisher();
        await publisher.PublishAsync(new TestIntegrationEvent("a"), CancellationToken.None);

        Assert.Throws<InvalidOperationException>(publisher.ShouldNotHavePublished<TestIntegrationEvent>);
    }

    [Fact]
    public async Task PublishAsync_WithConfigureCallback_StillRecordsEvent()
    {
        var publisher = new InMemoryEventPublisher();
        var configureCalled = false;

        await publisher.PublishAsync(new TestIntegrationEvent("a"), _ => configureCalled = true, CancellationToken.None);

        Assert.True(configureCalled);
        Assert.Single(publisher.Published);
    }

    // --- P-352/WO-054: PublishContext capture + IMessageHeaderPropagator application (T-73) ---

    [Fact]
    public async Task PublishAsync_NoConfigure_RunsPropagatorsInRegistrationOrder()
    {
        var executionLog = new List<string>();
        var first = new OrderTrackingPropagator("first", executionLog);
        var second = new OrderTrackingPropagator("second", executionLog);
        var publisher = new InMemoryEventPublisher([first, second]);

        await publisher.PublishAsync(new TestIntegrationEvent("a"), CancellationToken.None);

        Assert.Equal(["first", "second"], executionLog);
        var context = publisher.ShouldHavePublishedContext<TestIntegrationEvent>();
        Assert.Equal("first", context.Headers["x-propagator-first"]);
        Assert.Equal("second", context.Headers["x-propagator-second"]);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_RunsPropagatorsInRegistrationOrder_ThenConfigure()
    {
        var executionLog = new List<string>();
        var first = new OrderTrackingPropagator("first", executionLog);
        var second = new OrderTrackingPropagator("second", executionLog);
        var publisher = new InMemoryEventPublisher([first, second]);

        await publisher.PublishAsync(
            new TestIntegrationEvent("a"),
            ctx => ctx.WithHeader("x-configure", "explicit"),
            CancellationToken.None);

        Assert.Equal(["first", "second"], executionLog);
        var context = publisher.ShouldHavePublishedContext<TestIntegrationEvent>();
        Assert.Equal("first", context.Headers["x-propagator-first"]);
        Assert.Equal("second", context.Headers["x-propagator-second"]);
        Assert.Equal("explicit", context.Headers["x-configure"]);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_ExplicitCallbackWinsOverPropagatorOnSameKey()
    {
        var propagator = new HeaderSettingPropagator("x-conflict", "from-propagator");
        var publisher = new InMemoryEventPublisher([propagator]);

        await publisher.PublishAsync(
            new TestIntegrationEvent("a"),
            ctx => ctx.WithHeader("x-conflict", "from-configure"),
            CancellationToken.None);

        var context = publisher.ShouldHavePublishedContext<TestIntegrationEvent>();
        Assert.Equal("from-configure", context.Headers["x-conflict"]);
    }

    [Fact]
    public async Task PublishAsync_PropagatorSetsCorrelationCausationAndHeaders_RoundTripsThroughCapturedContext()
    {
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var propagator = new FullContextPropagator(correlationId, causationId, "x-custom", "custom-value");
        var publisher = new InMemoryEventPublisher([propagator]);

        await publisher.PublishAsync(new TestIntegrationEvent("a"), CancellationToken.None);

        var context = publisher.ShouldHavePublishedContext<TestIntegrationEvent>();
        Assert.Equal(correlationId, context.CorrelationId);
        Assert.Equal(causationId, context.CausationId);
        Assert.Equal("custom-value", context.Headers["x-custom"]);
    }

    [Fact]
    public async Task PublishAsync_NoPropagatorsRegistered_ConfigureCallbackAloneStillPopulatesContext()
    {
        var publisher = new InMemoryEventPublisher(); // default ctor — zero propagators, per P-352's own additive guarantee
        var correlationId = Guid.NewGuid();

        await publisher.PublishAsync(
            new TestIntegrationEvent("a"),
            ctx => ctx.WithCorrelationId(correlationId).WithHeader("k", "v"),
            CancellationToken.None);

        var context = publisher.ShouldHavePublishedContext<TestIntegrationEvent>();
        Assert.Equal(correlationId, context.CorrelationId);
        Assert.Equal("v", context.Headers["k"]);
    }

    [Fact]
    public void ShouldHavePublishedContext_NoMatch_Throws()
    {
        var publisher = new InMemoryEventPublisher();
        Assert.Throws<InvalidOperationException>(publisher.ShouldHavePublishedContext<TestIntegrationEvent>);
    }

    // --- Regression guard: .Published/.PublishedOf<TEvent>() must stay unchanged in return type and
    // ordering after the internal _published tuple-shape change (Event, PublishContext) that this
    // phase introduced — the one place a silent breaking change could hide, per T-73's own acceptance
    // criterion. ---

    [Fact]
    public async Task Published_And_PublishedOf_RemainUnchangedInTypeAndOrder_WhenPropagatorsAreRegistered()
    {
        var propagator = new OrderTrackingPropagator("only", []);
        var publisher = new InMemoryEventPublisher([propagator]);
        var first = new TestIntegrationEvent("a");
        var second = new TestIntegrationEvent("b");

        await publisher.PublishAsync(first, CancellationToken.None);
        await publisher.PublishAsync(second, ctx => ctx.WithHeader("k", "v"), CancellationToken.None);

        IReadOnlyList<object> published = publisher.Published;
        Assert.Equal(2, published.Count);
        Assert.Same(first, published[0]);
        Assert.Same(second, published[1]);

        IReadOnlyList<TestIntegrationEvent> typed = publisher.PublishedOf<TestIntegrationEvent>();
        Assert.Equal([first, second], typed);
    }

    // --- P-352/WO-054: PublishContext.TenantId/.PartitionKey round-trip (T-74) ---
    // 07.Messaging.Abstractions/EventPublisher/PublishContext.cs re-verified directly on disk before
    // writing these tests: TenantId (Guid?)/WithTenantId, PartitionKey (string?)/WithPartitionKey are
    // all real, shipped members (P-340/P-344/WO-054) — the T-74 blocker has cleared.

    [Fact]
    public async Task PublishAsync_NoConfigure_TenantPropagator_RoundTripsTenantIdThroughCapturedContext()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var publisher = new InMemoryEventPublisher([new TenantPropagator(tenantId)]);

        await publisher.PublishAsync(new TestIntegrationEvent("a"), CancellationToken.None);

        var context = publisher.ShouldHavePublishedContext<TestIntegrationEvent>();
        Assert.Equal(tenantId, context.TenantId);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_TenantPropagator_RoundTripsTenantIdThroughCapturedContext()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var publisher = new InMemoryEventPublisher([new TenantPropagator(tenantId)]);

        await publisher.PublishAsync(
            new TestIntegrationEvent("a"), ctx => ctx.WithHeader("k", "v"), CancellationToken.None);

        var context = publisher.ShouldHavePublishedContext<TestIntegrationEvent>();
        Assert.Equal(tenantId, context.TenantId);
        Assert.Equal("v", context.Headers["k"]);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_ExplicitTenantIdWinsOverPropagatorTenantId()
    {
        var propagatorTenantId = new TenantId(Guid.NewGuid());
        var explicitTenantId = new TenantId(Guid.NewGuid());
        var publisher = new InMemoryEventPublisher([new TenantPropagator(propagatorTenantId)]);

        await publisher.PublishAsync(
            new TestIntegrationEvent("a"), ctx => ctx.WithTenantId(explicitTenantId), CancellationToken.None);

        var context = publisher.ShouldHavePublishedContext<TestIntegrationEvent>();
        Assert.Equal(explicitTenantId, context.TenantId);
    }

    [Fact]
    public async Task PublishAsync_NoConfigure_PartitionKeyPropagator_RoundTripsPartitionKeyThroughCapturedContext()
    {
        var publisher = new InMemoryEventPublisher([new PartitionKeyPropagator("order-42")]);

        await publisher.PublishAsync(new TestIntegrationEvent("a"), CancellationToken.None);

        var context = publisher.ShouldHavePublishedContext<TestIntegrationEvent>();
        Assert.Equal("order-42", context.PartitionKey);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_PartitionKeyRoundTripsThroughCapturedContext()
    {
        var publisher = new InMemoryEventPublisher(); // no propagators — direct explicit configure only

        await publisher.PublishAsync(
            new TestIntegrationEvent("a"), ctx => ctx.WithPartitionKey("order-42"), CancellationToken.None);

        var context = publisher.ShouldHavePublishedContext<TestIntegrationEvent>();
        Assert.Equal("order-42", context.PartitionKey);
    }

    [Fact]
    public async Task PublishAsync_NoPropagatorsRegistered_ConfigureCallbackAlone_PopulatesTenantIdAndPartitionKey()
    {
        var publisher = new InMemoryEventPublisher(); // default ctor — zero propagators
        var tenantId = new TenantId(Guid.NewGuid());

        await publisher.PublishAsync(
            new TestIntegrationEvent("a"),
            ctx => ctx.WithTenantId(tenantId).WithPartitionKey("order-42"),
            CancellationToken.None);

        var context = publisher.ShouldHavePublishedContext<TestIntegrationEvent>();
        Assert.Equal(tenantId, context.TenantId);
        Assert.Equal("order-42", context.PartitionKey);
    }

    [Fact]
    public async Task PublishAsync_NoPropagatorsNoConfigure_TenantIdAndPartitionKeyAreNull()
    {
        var publisher = new InMemoryEventPublisher();

        await publisher.PublishAsync(new TestIntegrationEvent("a"), CancellationToken.None);

        var context = publisher.ShouldHavePublishedContext<TestIntegrationEvent>();
        Assert.Null(context.TenantId);
        Assert.Null(context.PartitionKey);
    }
}
