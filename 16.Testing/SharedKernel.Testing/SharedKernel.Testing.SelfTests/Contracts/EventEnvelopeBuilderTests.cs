using SharedKernel.Contracts.Events;
using SharedKernel.Testing.Contracts;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Contracts;

public sealed class EventEnvelopeBuilderTests
{
    [IntegrationEvent("tests.testing.contracts.envelope-builder-event", Version = 2)]
    private sealed record TestIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

    private sealed record UndeclaredIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

    private static TestIntegrationEvent NewEvent() => new(Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public void Build_WithDataOnly_UsesDefaults()
    {
        var data = NewEvent();

        var envelope = new EventEnvelopeBuilder<TestIntegrationEvent>().WithData(data).Build();

        Assert.Equal("test-service", envelope.Source);
        Assert.NotNull(envelope.CorrelationId);
        Assert.Null(envelope.CausationId);
        Assert.Null(envelope.Subject);
        Assert.Null(envelope.TenantId);
        Assert.Equal(data.EventId, envelope.Id);
        Assert.Equal(data.OccurredOn, envelope.Time);
        Assert.Same(data, envelope.Data);
    }

    [Fact]
    public void Build_TakesTypeAndDataVersionFromTheAttribute()
    {
        var envelope = new EventEnvelopeBuilder<TestIntegrationEvent>().WithData(NewEvent()).Build();

        Assert.Equal("tests.testing.contracts.envelope-builder-event", envelope.Type);
        Assert.Equal(2, envelope.DataVersion);
    }

    [Fact]
    public void Build_WithAllFieldsConfigured_PopulatesAllMetadata()
    {
        var tenantId = Guid.NewGuid();

        var envelope = new EventEnvelopeBuilder<TestIntegrationEvent>()
            .WithData(NewEvent())
            .WithSource("orders-service")
            .WithSubject("order/42")
            .WithTenantId(tenantId)
            .WithCorrelationId("corr-1")
            .WithCausationId("cause-1")
            .Build();

        Assert.Equal("orders-service", envelope.Source);
        Assert.Equal("order/42", envelope.Subject);
        Assert.Equal(tenantId, envelope.TenantId);
        Assert.Equal("corr-1", envelope.CorrelationId);
        Assert.Equal("cause-1", envelope.CausationId);
    }

    [Fact]
    public void Build_WithCorrelationIdCleared_OmitsIt()
    {
        var envelope = new EventEnvelopeBuilder<TestIntegrationEvent>()
            .WithData(NewEvent())
            .WithCorrelationId(null)
            .Build();

        Assert.Null(envelope.CorrelationId);
    }

    [Fact]
    public void Build_WithoutData_Throws()
    {
        var builder = new EventEnvelopeBuilder<TestIntegrationEvent>();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void WithData_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new EventEnvelopeBuilder<TestIntegrationEvent>().WithData(null!));

    [Fact]
    public void Build_WithBlankSource_ThrowsTheSameExceptionAsWrap()
    {
        var builder = new EventEnvelopeBuilder<TestIntegrationEvent>().WithData(NewEvent()).WithSource(" ");

        Assert.Throws<ArgumentException>(() => builder.Build());
    }

    [Fact]
    public void Build_WithEmptyTenantId_ThrowsTheSameExceptionAsWrap()
    {
        var builder = new EventEnvelopeBuilder<TestIntegrationEvent>().WithData(NewEvent()).WithTenantId(Guid.Empty);

        Assert.Throws<ArgumentException>(() => builder.Build());
    }

    [Fact]
    public void Build_ForAnEventWithoutTheAttribute_Throws()
    {
        var builder = new EventEnvelopeBuilder<UndeclaredIntegrationEvent>()
            .WithData(new UndeclaredIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow));

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }
}
