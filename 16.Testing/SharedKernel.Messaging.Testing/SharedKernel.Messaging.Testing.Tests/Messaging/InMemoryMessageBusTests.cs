using SharedKernel.Execution.Tenancy;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Testing.Messaging;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Messaging;

public sealed class InMemoryMessageBusTests
{
    private sealed record TestCommand(string Value);

    private sealed record TestEvent(string Value);

    private sealed record TestRequest(int X);

    private sealed record TestResponse(int Y);

    /// <summary>
    /// Test-double propagator that appends its <paramref name="name"/> to a shared,
    /// caller-supplied log every time <see cref="Propagate"/> runs — proves propagator
    /// EXECUTION ORDER independent of header-dictionary enumeration order — and writes a
    /// distinct, identifiable header so the resulting <see cref="PublishContext"/> can also
    /// be inspected directly.
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
    /// Used to prove TenantId round-trips through the captured context identically across every
    /// dispatch shape (T-74/P-352/WO-054).
    /// </summary>
    private sealed class TenantPropagator(TenantId tenantId) : IMessageHeaderPropagator
    {
        public void Propagate(PublishContext context) => context.WithTenantId(tenantId);
    }

    /// <summary>Test-double propagator that populates only <see cref="PublishContext.PartitionKey"/> via
    /// <see cref="PublishContext.WithPartitionKey"/> — proves PartitionKey round-trips through the
    /// captured context on dispatch verbs with no <c>configure</c> overload (SendAsync),
    /// mirroring <see cref="TenantPropagator"/>'s single-purpose shape (T-74/P-352/WO-054).</summary>
    private sealed class PartitionKeyPropagator(string partitionKey) : IMessageHeaderPropagator
    {
        public void Propagate(PublishContext context) => context.WithPartitionKey(partitionKey);
    }

    [Fact]
    public async Task PublishAsync_RecordsMessage_RetrievableViaShouldHavePublished()
    {
        var bus = new InMemoryMessageBus();
        var message = new TestEvent("hello");

        await bus.PublishAsync(message, CancellationToken.None);

        var found = bus.ShouldHavePublished<TestEvent>();
        Assert.Same(message, found);
    }

    [Fact]
    public async Task SendAsync_RecordsCommand_RetrievableViaShouldHaveSent()
    {
        var bus = new InMemoryMessageBus();
        var command = new TestCommand("do-it");

        await bus.SendAsync(command, CancellationToken.None);

        var found = bus.ShouldHaveSent<TestCommand>();
        Assert.Same(command, found);
    }

    [Fact]
    public void ShouldHavePublished_NoMatch_Throws()
    {
        var bus = new InMemoryMessageBus();
        Assert.Throws<InvalidOperationException>(bus.ShouldHavePublished<TestEvent>);
    }

    [Fact]
    public void ShouldHaveSent_NoMatch_Throws()
    {
        var bus = new InMemoryMessageBus();
        Assert.Throws<InvalidOperationException>(bus.ShouldHaveSent<TestCommand>);
    }

    [Fact]
    public async Task ShouldHavePublishedOnce_ExactlyOne_ReturnsIt()
    {
        var bus = new InMemoryMessageBus();
        await bus.PublishAsync(new TestEvent("a"), CancellationToken.None);

        var found = bus.ShouldHavePublishedOnce<TestEvent>();
        Assert.Equal("a", found.Value);
    }

    [Fact]
    public async Task ShouldHavePublishedOnce_MoreThanOne_Throws()
    {
        var bus = new InMemoryMessageBus();
        await bus.PublishAsync(new TestEvent("a"), CancellationToken.None);
        await bus.PublishAsync(new TestEvent("b"), CancellationToken.None);

        Assert.Throws<InvalidOperationException>(bus.ShouldHavePublishedOnce<TestEvent>);
    }

    [Fact]
    public void ShouldHavePublishedOnce_Zero_Throws()
    {
        var bus = new InMemoryMessageBus();
        Assert.Throws<InvalidOperationException>(bus.ShouldHavePublishedOnce<TestEvent>);
    }

    [Fact]
    public void ShouldNotHavePublished_NothingPublished_DoesNotThrow()
    {
        var bus = new InMemoryMessageBus();
        bus.ShouldNotHavePublished<TestEvent>();
    }

    [Fact]
    public async Task ShouldNotHavePublished_SomethingPublished_Throws()
    {
        var bus = new InMemoryMessageBus();
        await bus.PublishAsync(new TestEvent("a"), CancellationToken.None);

        Assert.Throws<InvalidOperationException>(bus.ShouldNotHavePublished<TestEvent>);
    }

    [Fact]
    public async Task PublishAsync_RecordsEvenWithoutAssertion_DoesNotMutateOnQuery()
    {
        var bus = new InMemoryMessageBus();
        await bus.PublishAsync(new TestEvent("a"), CancellationToken.None);

        // Querying twice must not change observable state.
        bus.ShouldHavePublished<TestEvent>();
        var second = bus.ShouldHavePublished<TestEvent>();

        Assert.Equal("a", second.Value);
    }

    // --- P-352/WO-054: PublishContext capture + IMessageHeaderPropagator application (T-72) ---

    [Fact]
    public async Task PublishAsync_NoConfigure_RunsPropagatorsInRegistrationOrder()
    {
        var executionLog = new List<string>();
        var first = new OrderTrackingPropagator("first", executionLog);
        var second = new OrderTrackingPropagator("second", executionLog);
        var bus = new InMemoryMessageBus([first, second]);

        await bus.PublishAsync(new TestEvent("a"), CancellationToken.None);

        Assert.Equal(["first", "second"], executionLog);
        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Equal("first", context.Headers["x-propagator-first"]);
        Assert.Equal("second", context.Headers["x-propagator-second"]);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_RunsPropagatorsInRegistrationOrder_ThenConfigure()
    {
        var executionLog = new List<string>();
        var first = new OrderTrackingPropagator("first", executionLog);
        var second = new OrderTrackingPropagator("second", executionLog);
        var bus = new InMemoryMessageBus([first, second]);

        await bus.PublishAsync(
            new TestEvent("a"),
            ctx => ctx.WithHeader("x-configure", "explicit"),
            CancellationToken.None);

        Assert.Equal(["first", "second"], executionLog);
        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Equal("first", context.Headers["x-propagator-first"]);
        Assert.Equal("second", context.Headers["x-propagator-second"]);
        Assert.Equal("explicit", context.Headers["x-configure"]);
    }

    [Fact]
    public async Task SendAsync_RunsPropagatorsInRegistrationOrder()
    {
        var executionLog = new List<string>();
        var first = new OrderTrackingPropagator("first", executionLog);
        var second = new OrderTrackingPropagator("second", executionLog);
        var bus = new InMemoryMessageBus([first, second]);

        await bus.SendAsync(new TestCommand("do-it"), CancellationToken.None);

        Assert.Equal(["first", "second"], executionLog);
        var context = bus.ShouldHaveSentContext<TestCommand>();
        Assert.Equal("first", context.Headers["x-propagator-first"]);
        Assert.Equal("second", context.Headers["x-propagator-second"]);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_ExplicitCallbackWinsOverPropagatorOnSameKey()
    {
        var propagator = new HeaderSettingPropagator("x-conflict", "from-propagator");
        var bus = new InMemoryMessageBus([propagator]);

        await bus.PublishAsync(
            new TestEvent("a"),
            ctx => ctx.WithHeader("x-conflict", "from-configure"),
            CancellationToken.None);

        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Equal("from-configure", context.Headers["x-conflict"]);
    }

    [Fact]
    public async Task PublishAsync_PropagatorSetsCorrelationCausationAndHeaders_RoundTripsThroughCapturedContext()
    {
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var propagator = new FullContextPropagator(correlationId, causationId, "x-custom", "custom-value");
        var bus = new InMemoryMessageBus([propagator]);

        await bus.PublishAsync(new TestEvent("a"), CancellationToken.None);

        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Equal(correlationId, context.CorrelationId);
        Assert.Equal(causationId, context.CausationId);
        Assert.Equal("custom-value", context.Headers["x-custom"]);
    }

    [Fact]
    public async Task SendAsync_PropagatorSetsCorrelationCausationAndHeaders_RoundTripsThroughCapturedContext()
    {
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var propagator = new FullContextPropagator(correlationId, causationId, "x-custom", "custom-value");
        var bus = new InMemoryMessageBus([propagator]);

        await bus.SendAsync(new TestCommand("do-it"), CancellationToken.None);

        var context = bus.ShouldHaveSentContext<TestCommand>();
        Assert.Equal(correlationId, context.CorrelationId);
        Assert.Equal(causationId, context.CausationId);
        Assert.Equal("custom-value", context.Headers["x-custom"]);
    }

    [Fact]
    public async Task PublishAsync_NoPropagatorsRegistered_ConfigureCallbackAloneStillPopulatesContext()
    {
        var bus = new InMemoryMessageBus(); // default ctor — zero propagators, per P-352's own additive guarantee
        var correlationId = Guid.NewGuid();

        await bus.PublishAsync(
            new TestEvent("a"),
            ctx => ctx.WithCorrelationId(correlationId).WithHeader("k", "v"),
            CancellationToken.None);

        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Equal(correlationId, context.CorrelationId);
        Assert.Equal("v", context.Headers["k"]);
    }

    [Fact]
    public void ShouldHavePublishedContext_NoMatch_Throws()
    {
        var bus = new InMemoryMessageBus();
        Assert.Throws<InvalidOperationException>(bus.ShouldHavePublishedContext<TestEvent>);
    }

    [Fact]
    public void ShouldHaveSentContext_NoMatch_Throws()
    {
        var bus = new InMemoryMessageBus();
        Assert.Throws<InvalidOperationException>(bus.ShouldHaveSentContext<TestCommand>);
    }

    [Fact]
    public async Task PublishAsync_NoConfigure_TenantPropagator_RoundTripsTenantIdThroughCapturedContext()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var bus = new InMemoryMessageBus([new TenantPropagator(tenantId)]);

        await bus.PublishAsync(new TestEvent("a"), CancellationToken.None);

        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Equal(tenantId, context.TenantId);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_TenantPropagator_RoundTripsTenantIdThroughCapturedContext()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var bus = new InMemoryMessageBus([new TenantPropagator(tenantId)]);

        await bus.PublishAsync(new TestEvent("a"), ctx => ctx.WithHeader("k", "v"), CancellationToken.None);

        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Equal(tenantId, context.TenantId);
        Assert.Equal("v", context.Headers["k"]);
    }

    [Fact]
    public async Task SendAsync_TenantPropagator_RoundTripsTenantIdThroughCapturedContext()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var bus = new InMemoryMessageBus([new TenantPropagator(tenantId)]);

        await bus.SendAsync(new TestCommand("do-it"), CancellationToken.None);

        var context = bus.ShouldHaveSentContext<TestCommand>();
        Assert.Equal(tenantId, context.TenantId);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_ExplicitTenantIdWinsOverPropagatorTenantId()
    {
        var propagatorTenantId = new TenantId(Guid.NewGuid());
        var explicitTenantId = new TenantId(Guid.NewGuid());
        var bus = new InMemoryMessageBus([new TenantPropagator(propagatorTenantId)]);

        await bus.PublishAsync(
            new TestEvent("a"),
            ctx => ctx.WithTenantId(explicitTenantId),
            CancellationToken.None);

        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Equal(explicitTenantId, context.TenantId);
    }

    [Fact]
    public async Task PublishAsync_NoConfigure_PartitionKeyPropagator_RoundTripsPartitionKeyThroughCapturedContext()
    {
        var bus = new InMemoryMessageBus([new PartitionKeyPropagator("order-42")]);

        await bus.PublishAsync(new TestEvent("a"), CancellationToken.None);

        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Equal("order-42", context.PartitionKey);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_PartitionKeyRoundTripsThroughCapturedContext()
    {
        var bus = new InMemoryMessageBus(); // no propagators — direct explicit configure only

        await bus.PublishAsync(
            new TestEvent("a"),
            ctx => ctx.WithPartitionKey("order-42"),
            CancellationToken.None);

        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Equal("order-42", context.PartitionKey);
    }

    [Fact]
    public async Task SendAsync_PartitionKeyPropagator_RoundTripsPartitionKeyThroughCapturedContext()
    {
        var bus = new InMemoryMessageBus([new PartitionKeyPropagator("order-42")]);

        await bus.SendAsync(new TestCommand("do-it"), CancellationToken.None);

        var context = bus.ShouldHaveSentContext<TestCommand>();
        Assert.Equal("order-42", context.PartitionKey);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_ExplicitPartitionKeyWinsOverPropagatorPartitionKey()
    {
        var bus = new InMemoryMessageBus([new PartitionKeyPropagator("from-propagator")]);

        await bus.PublishAsync(
            new TestEvent("a"),
            ctx => ctx.WithPartitionKey("from-configure"),
            CancellationToken.None);

        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Equal("from-configure", context.PartitionKey);
    }

    [Fact]
    public async Task PublishAsync_NoPropagatorsRegistered_ConfigureCallbackAlone_PopulatesTenantIdAndPartitionKey()
    {
        var bus = new InMemoryMessageBus(); // default ctor — zero propagators
        var tenantId = new TenantId(Guid.NewGuid());

        await bus.PublishAsync(
            new TestEvent("a"),
            ctx => ctx.WithTenantId(tenantId).WithPartitionKey("order-42"),
            CancellationToken.None);

        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Equal(tenantId, context.TenantId);
        Assert.Equal("order-42", context.PartitionKey);
    }

    [Fact]
    public async Task PublishAsync_NoPropagatorsNoConfigure_TenantIdAndPartitionKeyAreNull()
    {
        var bus = new InMemoryMessageBus();

        await bus.PublishAsync(new TestEvent("a"), CancellationToken.None);

        var context = bus.ShouldHavePublishedContext<TestEvent>();
        Assert.Null(context.TenantId);
        Assert.Null(context.PartitionKey);
    }
}
