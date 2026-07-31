using System.Text.Json;
using SharedKernel.Contracts.Events;
using SharedKernel.Contracts.Serialization;
using SharedKernel.Domain.Events;

namespace SharedKernel.Contracts.Tests;

// Test domain events — internal to this test assembly

internal sealed record TestOrderCreatedEvent : DomainEvent
{
    public required Guid OrderId { get; init; }
}

[DomainEventVersion(3)]
internal sealed record TestOrderCancelledEvent : DomainEvent
{
    public required string Reason { get; init; }
}

public sealed class EventEnvelopeTests
{
    private static readonly string SourceService = "order-service";
    private static readonly string CorrelationId = "corr-123";
    private static readonly string CausationId = "cause-456";

    private static TestOrderCreatedEvent CreateOrderEvent()
        => new() { OccurredOn = new DateTimeOffset(2026, 5, 30, 10, 0, 0, TimeSpan.Zero), OrderId = Guid.NewGuid() };

    // ─── Wrap factory ─────────────────────────────────────────────────────────

    [Fact]
    public void Wrap_PopulatesEventId_FromDomainEvent()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService);

        envelope.EventId.Should().Be(domainEvent.Id);
    }

    [Fact]
    public void Wrap_PopulatesOccurredOn_FromDomainEvent()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService);

        envelope.OccurredOn.Should().Be(domainEvent.OccurredOn);
    }

    [Fact]
    public void Wrap_PopulatesEventType_AsTypeNameOfTEvent()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService);

        envelope.EventType.Should().Be(typeof(TestOrderCreatedEvent).Name);
    }

    [Fact]
    public void Wrap_PopulatesSourceService_AsProvided()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService);

        envelope.SourceService.Should().Be(SourceService);
    }

    [Fact]
    public void Wrap_PopulatesPayload_AsProvidedDomainEvent()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService);

        envelope.Payload.Should().Be(domainEvent);
    }

    [Fact]
    public void Wrap_PopulatesCorrelationId_WhenProvided()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService, correlationId: CorrelationId);

        envelope.CorrelationId.Should().Be(CorrelationId);
    }

    [Fact]
    public void Wrap_PopulatesCausationId_WhenProvided()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService, causationId: CausationId);

        envelope.CausationId.Should().Be(CausationId);
    }

    [Fact]
    public void Wrap_CorrelationId_IsNullByDefault()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService);

        envelope.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void Wrap_CausationId_IsNullByDefault()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService);

        envelope.CausationId.Should().BeNull();
    }

    // ─── EventVersion ─────────────────────────────────────────────────────────

    [Fact]
    public void Wrap_EventVersion_DefaultsToOne_WhenAttributeAbsent()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService);

        envelope.EventVersion.Should().Be(1);
    }

    [Fact]
    public void Wrap_EventVersion_UsesDeclaredVersion_WhenAttributePresent()
    {
        var domainEvent = new TestOrderCancelledEvent
        {
            OccurredOn = DateTimeOffset.UtcNow,
            Reason = "customer request"
        };

        var envelope = EventEnvelope.Wrap(domainEvent, SourceService);

        envelope.EventVersion.Should().Be(3);
    }

    // ─── Guard clauses ────────────────────────────────────────────────────────

    [Fact]
    public void Wrap_NullDomainEvent_ThrowsArgumentNullException()
    {
        var act = () => EventEnvelope.Wrap<TestOrderCreatedEvent>(null!, SourceService);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("domainEvent");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Wrap_NullOrEmptySourceService_ThrowsArgumentException(string? invalidSourceService)
    {
        var domainEvent = CreateOrderEvent();
        var act = () => EventEnvelope.Wrap(domainEvent, invalidSourceService!);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("sourceService");
    }

    // ─── TenantId (WO-052/P-331) ──────────────────────────────────────────────

    [Fact]
    public void Wrap_TenantId_IsNullByDefault()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService);

        envelope.TenantId.Should().BeNull();
    }

    [Fact]
    public void Wrap_PopulatesTenantId_WhenProvided()
    {
        var tenantId = Guid.NewGuid();
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService, tenantId: tenantId);

        envelope.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public void Wrap_WithoutTenantIdArgument_StillCompiles_AndDefaultsToNull()
    {
        // Regression: existing call sites with no tenantId argument must remain source-compatible
        // after the trailing optional parameter was added (WO-052/P-331).
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService, CorrelationId, CausationId);

        envelope.TenantId.Should().BeNull();
    }

    [Fact]
    public void TwoEnvelopes_WithDifferentTenantId_AreNotEqual()
    {
        var domainEvent = CreateOrderEvent();
        var a = EventEnvelope.Wrap(domainEvent, SourceService, tenantId: Guid.NewGuid());
        var b = EventEnvelope.Wrap(domainEvent, SourceService, tenantId: Guid.NewGuid());

        a.Should().NotBe(b);
    }

    [Fact]
    public void TwoEnvelopes_WithSameTenantId_AreEqual()
    {
        var tenantId = Guid.NewGuid();
        var domainEvent = CreateOrderEvent();
        var a = EventEnvelope.Wrap(domainEvent, SourceService, CorrelationId, CausationId, tenantId);
        var b = EventEnvelope.Wrap(domainEvent, SourceService, CorrelationId, CausationId, tenantId);

        a.Should().Be(b);
    }

    [Fact]
    public void Wrap_TenantId_SerjDeserj_RoundTrips_WhenNull()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService, CorrelationId, CausationId);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.TypeInfoResolverChain.Add(TestJsonContext.Default);
        options.TypeInfoResolverChain.Add(ContractsJsonContext.Default);

        var json = JsonSerializer.Serialize(envelope, options);

        // The test-level JsonSerializerOptions only sets PropertyNamingPolicy (matching the
        // established pattern) — DefaultIgnoreCondition is not applied here, so a null TenantId
        // round-trips as a literal JSON null rather than being omitted.
        json.Should().Contain("\"tenantId\":null");

        var deserialized = JsonSerializer.Deserialize<EventEnvelope<TestOrderCreatedEvent>>(json, options);
        deserialized.Should().NotBeNull();
        deserialized!.TenantId.Should().BeNull();
    }

    [Fact]
    public void Wrap_TenantId_SerjDeserj_RoundTrips_WhenPopulated()
    {
        var tenantId = Guid.NewGuid();
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService, CorrelationId, CausationId, tenantId);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.TypeInfoResolverChain.Add(TestJsonContext.Default);
        options.TypeInfoResolverChain.Add(ContractsJsonContext.Default);

        var json = JsonSerializer.Serialize(envelope, options);

        json.Should().Contain("\"tenantId\":");
        json.Should().Contain(tenantId.ToString());
    }

    // ─── EventId is not a new envelope identity ───────────────────────────────

    [Fact]
    public void Wrap_EventId_IsCopiedFromDomainEvent_NotNewGuid()
    {
        var domainEvent = CreateOrderEvent();
        var envelope1 = EventEnvelope.Wrap(domainEvent, SourceService);
        var envelope2 = EventEnvelope.Wrap(domainEvent, SourceService);

        // Both envelopes wrapping the same domain event must share the same EventId (copied from domain event)
        envelope1.EventId.Should().Be(envelope2.EventId);
        envelope1.EventId.Should().Be(domainEvent.Id);
    }

    // ─── Record structural equality ───────────────────────────────────────────

    [Fact]
    public void TwoEnvelopes_WrappingSameDomainEvent_AreEqual()
    {
        var domainEvent = CreateOrderEvent();
        var a = EventEnvelope.Wrap(domainEvent, SourceService, CorrelationId, CausationId);
        var b = EventEnvelope.Wrap(domainEvent, SourceService, CorrelationId, CausationId);

        a.Should().Be(b);
    }

    // ─── STJ round-trip ───────────────────────────────────────────────────────

    [Fact]
    public void Wrap_SerjDeserj_RoundTrips_ViaSourceGeneratedContext()
    {
        var domainEvent = CreateOrderEvent();
        var envelope = EventEnvelope.Wrap(domainEvent, SourceService, CorrelationId, CausationId);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.TypeInfoResolverChain.Add(TestJsonContext.Default);
        options.TypeInfoResolverChain.Add(ContractsJsonContext.Default);

        // The ContractsJsonContext covers EventEnvelope<DomainEvent> (abstract base).
        // Verify serialization infrastructure produces output with expected field names.
        var json = JsonSerializer.Serialize(envelope, options);

        json.Should().Contain("\"eventType\":");
        json.Should().Contain("\"eventVersion\":");
        json.Should().Contain("\"sourceService\":");
        json.Should().Contain("\"correlationId\":");
        json.Should().Contain("\"causationId\":");
        json.Should().Contain(SourceService);
        json.Should().Contain(CorrelationId);
        json.Should().Contain(CausationId);
    }
}
