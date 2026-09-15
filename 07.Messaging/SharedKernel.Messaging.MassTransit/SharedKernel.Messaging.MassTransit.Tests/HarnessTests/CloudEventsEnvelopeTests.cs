#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.EventPublisher;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// T-08: CloudEvents envelope tests.
/// Verifies the published envelope's CloudEvents attributes: <c>source</c>, <c>type</c>, <c>dataversion</c>,
/// <c>id</c>, <c>time</c>, <c>subject</c>, and the <c>tenantid</c>/<c>correlationid</c>/<c>causationid</c>
/// extensions. Uses MassTransit TestHarness (in-memory) to intercept the outgoing EventEnvelope&lt;TEvent&gt;.
/// </summary>
public sealed class CloudEventsEnvelopeTests
{
    private const string OrderPlacedEventName = "tests.messaging.cloud-events.order-placed";

    [Fact]
    public async Task PublishAsync_PopulatesSource()
    {
        await using var provider = BuildProvider("envelope-source-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();
        envelope.Context.Message.Source.Should().Be("envelope-source-service");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_PopulatesTypeAndSpecVersion_FromIntegrationEventAttribute()
    {
        await using var provider = BuildProvider("envelope-type-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First().Context.Message;
        envelope.Type.Should().Be(OrderPlacedEventName,
            "the CloudEvents type is the [IntegrationEvent] name, never the CLR class name");
        envelope.SpecVersion.Should().Be(EventEnvelope.CloudEventsSpecVersion);
        envelope.DataContentType.Should().Be(EventEnvelope.JsonContentType);

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_PopulatesNonEmptyCorrelationId()
    {
        await using var provider = BuildProvider("envelope-correlation-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();
        envelope.Context.Message.CorrelationId.Should().NotBeNullOrEmpty(
            "CorrelationId must always be populated (from Activity or Guid.NewGuid fallback)");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_PopulatesDataVersion_FromIntegrationEventAttribute()
    {
        await using var provider = BuildProvider("envelope-schema-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = VersionedOrderPlacedEvent.Create();

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<VersionedOrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<VersionedOrderPlacedEvent>>().First();

        envelope.Context.Message.DataVersion.Should().Be(3,
            "dataversion comes from [IntegrationEvent(Version = 3)]");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_WithoutVersionDeclared_DataVersionDefaultsTo1()
    {
        await using var provider = BuildProvider("envelope-default-schema-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();

        envelope.Context.Message.DataVersion.Should().Be(1);

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_PopulatesIdAndTime_FromEvent()
    {
        await using var provider = BuildProvider("envelope-timestamp-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First().Context.Message;

        // id and time are sourced from the integration event itself, never generated at publish time.
        envelope.Id.Should().Be(evt.EventId);
        envelope.Time.Should().Be(evt.OccurredOn);
        envelope.Data.Should().Be(evt);

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_WithExplicitCorrelationId_UsesProvidedValue()
    {
        await using var provider = BuildProvider("envelope-explicit-corr-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();
        var explicitCorrelationId = Guid.NewGuid();

        await publisher.PublishAsync(evt, ctx => ctx.WithCorrelationId(explicitCorrelationId), CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();
        envelope.Context.Message.CorrelationId.Should().Be(explicitCorrelationId.ToString("D"),
            "explicit CorrelationId override from PublishContext must be used");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_WithCausationId_SetsCausationId()
    {
        await using var provider = BuildProvider("envelope-causation-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();
        var causationId = Guid.NewGuid();

        await publisher.PublishAsync(evt, ctx => ctx.WithCausationId(causationId), CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();
        envelope.Context.Message.CausationId.Should().Be(causationId.ToString("D"));

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_WithoutCausationId_CausationIdIsNull()
    {
        await using var provider = BuildProvider("envelope-no-causation-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();
        envelope.Context.Message.CausationId.Should().BeNull();

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_WithTenantId_UsesProvidedValue()
    {
        await using var provider = BuildProvider("envelope-tenant-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();
        var tenantId = Guid.NewGuid();

        await publisher.PublishAsync(evt, ctx => ctx.WithTenantId(tenantId), CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();
        envelope.Context.Message.TenantId.Should().Be(tenantId,
            "explicit TenantId set via PublishContext.WithTenantId must flow into EventEnvelope<TEvent>.TenantId");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_WithoutTenantId_TenantIdIsNull()
    {
        await using var provider = BuildProvider("envelope-no-tenant-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();
        envelope.Context.Message.TenantId.Should().BeNull(
            "an unset PublishContext.TenantId must omit TenantId, exactly like an unset CausationId");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_WithSubject_SetsSubject()
    {
        await using var provider = BuildProvider("envelope-subject-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();
        var subject = $"order/{evt.OrderId:D}";

        await publisher.PublishAsync(evt, ctx => ctx.WithSubject(subject), CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();
        envelope.Context.Message.Subject.Should().Be(subject,
            "PublishContext.WithSubject must flow into the CloudEvents subject attribute");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_WithoutSubject_SubjectIsNull()
    {
        await using var provider = BuildProvider("envelope-no-subject-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();
        envelope.Context.Message.Subject.Should().BeNull();

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_ConstructsEnvelopeExclusivelyViaWrapFactory()
    {
        // T-340/ET-07: proves factory-only construction is actually exercised, not merely
        // asserted by inspection. Independently builds the expected envelope via
        // EventEnvelope.Wrap<TEvent>() using the exact same inputs the publisher
        // received, then asserts the captured published envelope is record-equal to it.
        // A regression that omits or mis-populates any field — including swapping two of the
        // optional string arguments — would fail this whole-record comparison even though
        // narrower field-by-field assertions might not catch it.
        const string sourceService = "envelope-factory-only-service";
        await using var provider = BuildProvider(sourceService);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create();
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var subject = $"order/{evt.OrderId:D}";

        await publisher.PublishAsync(
            evt,
            ctx => ctx
                .WithCorrelationId(correlationId)
                .WithCausationId(causationId)
                .WithTenantId(tenantId)
                .WithSubject(subject),
            CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var actual = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First().Context.Message;

        var expected = EventEnvelope.Wrap(
            evt,
            source: sourceService,
            subject: subject,
            tenantId: tenantId,
            correlationId: correlationId.ToString("D"),
            causationId: causationId.ToString("D"));

        actual.Should().Be(expected,
            "the envelope must be constructed exclusively via EventEnvelope.Wrap<TEvent>() — " +
            "any drift back to a raw object initializer would produce a non-record-equal result");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_EventWithoutIntegrationEventAttribute_ThrowsInvalidOperationException()
    {
        // The event type implements IIntegrationEvent (so it compiles) but declares no wire name.
        // The publisher must refuse it rather than fall back to the CLR class name.
        await using var provider = BuildProvider("envelope-invalid-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new UndeclaredEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var act = async () => await publisher.PublishAsync(evt, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*[IntegrationEvent*");
        (await harness.Published.Any<EventEnvelope<UndeclaredEvent>>()).Should().BeFalse(
            "nothing may reach the transport for an event type with no declared wire name");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_EventWithEmptyEventId_ThrowsArgumentException()
    {
        await using var provider = BuildProvider("envelope-empty-id-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = OrderPlacedEvent.Create() with { EventId = Guid.Empty };

        var act = async () => await publisher.PublishAsync(evt, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // Private helper — builds an in-memory test provider with IEventPublisher
    // -------------------------------------------------------------------------

    private static ServiceProvider BuildProvider(string serviceName)
    {
        var services = new ServiceCollection();

        // Register MassTransit with in-memory TestHarness
        services.AddMassTransitTestHarness();

        // Register MessagingOptions and IEventPublisher (the real implementation)
        services.Configure<MessagingOptions>(o => o.ServiceName = serviceName);
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        return services.BuildServiceProvider(true);
    }
}

// ---------------------------------------------------------------------------
// Integration events used in tests
// ---------------------------------------------------------------------------

[IntegrationEvent("tests.messaging.cloud-events.order-placed")]
file sealed record OrderPlacedEvent(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent
{
    public static OrderPlacedEvent Create() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());
}

[IntegrationEvent("tests.messaging.cloud-events.versioned-order-placed", Version = 3)]
file sealed record VersionedOrderPlacedEvent(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent
{
    public static VersionedOrderPlacedEvent Create() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());
}

// Deliberately has no [IntegrationEvent] attribute.
file sealed record UndeclaredEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
