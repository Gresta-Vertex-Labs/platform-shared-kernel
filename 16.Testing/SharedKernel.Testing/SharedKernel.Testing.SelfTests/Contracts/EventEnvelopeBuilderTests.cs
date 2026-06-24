using SharedKernel.Domain.Events;
using SharedKernel.Testing.Contracts;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Contracts;

public sealed class EventEnvelopeBuilderTests
{
    private sealed record TestDomainEvent(Guid Id, DateTimeOffset OccurredOn) : IDomainEvent;

    [Fact]
    public void Build_WithPayloadOnly_UsesDefaults()
    {
        var payload = new TestDomainEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var envelope = new EventEnvelopeBuilder<TestDomainEvent>().WithPayload(payload).Build();

        Assert.Equal("test-service", envelope.SourceService);
        Assert.NotNull(envelope.CorrelationId);
        Assert.Null(envelope.CausationId);
        Assert.Equal(payload.Id, envelope.EventId);
    }

    [Fact]
    public void Build_WithAllFieldsConfigured_PopulatesAllMetadata()
    {
        var payload = new TestDomainEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var envelope = new EventEnvelopeBuilder<TestDomainEvent>()
            .WithPayload(payload)
            .WithSourceService("orders-service")
            .WithCorrelationId("corr-1")
            .WithCausationId("cause-1")
            .Build();

        Assert.Equal("orders-service", envelope.SourceService);
        Assert.Equal("corr-1", envelope.CorrelationId);
        Assert.Equal("cause-1", envelope.CausationId);
    }

    [Fact]
    public void Build_WithoutPayload_Throws()
    {
        var builder = new EventEnvelopeBuilder<TestDomainEvent>();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void WithSourceService_NullOrWhitespace_Throws()
    {
        var builder = new EventEnvelopeBuilder<TestDomainEvent>();

        Assert.Throws<ArgumentException>(() => builder.WithSourceService(" "));
    }
}
