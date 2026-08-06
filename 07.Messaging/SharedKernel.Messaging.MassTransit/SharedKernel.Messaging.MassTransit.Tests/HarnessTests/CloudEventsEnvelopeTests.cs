#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Contracts.Events;
using SharedKernel.Domain.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.EventPublisher;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// T-08: CloudEvents envelope tests.
/// Verifies all five fields: SourceService, CorrelationId non-empty, SchemaVersion, TimestampUtc recent.
/// Uses MassTransit TestHarness (in-memory) to intercept the outgoing EventEnvelope&lt;TEvent&gt;.
/// </summary>
public sealed class CloudEventsEnvelopeTests
{
    [Fact]
    public async Task PublishAsync_PopulatesSourceService()
    {
        await using var provider = BuildProvider("envelope-source-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new OrderPlacedEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();
        envelope.Context.Message.SourceService.Should().Be("envelope-source-service");

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
        var evt = new OrderPlacedEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();
        envelope.Context.Message.CorrelationId.Should().NotBeNullOrEmpty(
            "CorrelationId must always be populated (from Activity or Guid.NewGuid fallback)");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_PopulatesSchemaVersionDefault1()
    {
        await using var provider = BuildProvider("envelope-schema-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new OrderPlacedEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();

        // DomainEventVersionHelper.GetVersion returns 1 for events without [DomainEventVersion] attribute
        envelope.Context.Message.EventVersion.Should().Be(1);

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_PopulatesTimestampUtcRecently()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        await using var provider = BuildProvider("envelope-timestamp-service");
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new OrderPlacedEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();

        // OccurredOn is sourced from the domain event payload — must be after the test start
        envelope.Context.Message.OccurredOn.Should().BeAfter(before);

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
        var evt = new OrderPlacedEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };
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
        var evt = new OrderPlacedEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };
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
        var evt = new OrderPlacedEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };

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
        var evt = new OrderPlacedEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };
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
        var evt = new OrderPlacedEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First();
        envelope.Context.Message.TenantId.Should().BeNull(
            "an unset PublishContext.TenantId must omit TenantId, exactly like an unset CausationId");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_ConstructsEnvelopeExclusivelyViaWrapFactory()
    {
        // T-340/ET-07: proves factory-only construction is actually exercised, not merely
        // asserted by inspection. Independently builds the expected envelope via
        // EventEnvelope.Wrap<TEvent>() using the exact same inputs PublishEnvelope<TEvent>
        // received, then asserts the captured published envelope is record-equal to it.
        // A regression back to a hand-built object initializer that omits or mis-populates
        // any field would fail this whole-record comparison even though narrower
        // field-by-field assertions would not catch it.
        const string sourceService = "envelope-factory-only-service";
        await using var provider = BuildProvider(sourceService);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new OrderPlacedEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await publisher.PublishAsync(
            evt,
            ctx => ctx.WithCorrelationId(correlationId).WithCausationId(causationId).WithTenantId(tenantId),
            CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<OrderPlacedEvent>>()).Should().BeTrue();
        var actual = harness.Published.Select<EventEnvelope<OrderPlacedEvent>>().First().Context.Message;

        var expected = EventEnvelope.Wrap(
            evt,
            sourceService,
            correlationId.ToString("D"),
            causationId.ToString("D"),
            tenantId);

        actual.Should().Be(expected,
            "the envelope must be constructed exclusively via EventEnvelope.Wrap<TEvent>() — " +
            "any drift back to a raw object initializer would produce a non-record-equal result");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_NonDomainEvent_ThrowsInvalidOperationException()
    {
        // Tests the runtime guard in MassTransitEventPublisher directly without the harness.
        var services = new ServiceCollection();
        services.AddMassTransitTestHarness();
        services.Configure<MessagingOptions>(o => o.ServiceName = "envelope-invalid-service");
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        await using var sp = services.BuildServiceProvider(true);
        using var scope = sp.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var plainObject = new PlainMessage("not-a-domain-event");

        var act = async () => await publisher.PublishAsync(plainObject, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IDomainEvent*");
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
// Domain event used in tests
// ---------------------------------------------------------------------------

file sealed record OrderPlacedEvent : DomainEvent
{
    public Guid OrderId { get; init; }
}

file sealed record PlainMessage(string Text);
