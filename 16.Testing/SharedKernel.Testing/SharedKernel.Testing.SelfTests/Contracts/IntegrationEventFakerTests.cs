using SharedKernel.Contracts.Events;
using SharedKernel.Testing.Contracts;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Contracts;

public sealed class IntegrationEventFakerTests
{
    private sealed record TestIntegrationEvent : IIntegrationEvent
    {
        public Guid EventId { get; init; }
        public DateTimeOffset OccurredOn { get; init; }
        public string? Payload { get; init; }
    }

    private sealed class TestIntegrationEventFaker : IntegrationEventFaker<TestIntegrationEvent>
    {
        public TestIntegrationEventFaker()
        {
            RuleForEventId();
            RuleForOccurredOn();
            RuleFor(e => e.Payload, f => f.Lorem.Word());
        }
    }

    [Fact]
    public void RuleForEventId_PopulatesNonEmptyGuid()
    {
        var generated = new TestIntegrationEventFaker().Generate();

        Assert.NotEqual(Guid.Empty, generated.EventId);
    }

    [Fact]
    public void RuleForOccurredOn_PopulatesRecentTimestamp()
    {
        var generated = new TestIntegrationEventFaker().Generate();

        Assert.True(generated.OccurredOn <= DateTimeOffset.UtcNow);
        Assert.True(generated.OccurredOn > DateTimeOffset.UtcNow.AddYears(-1));
    }

    [Fact]
    public void SubclassRuleFor_AdditionalField_IsPopulated()
    {
        var generated = new TestIntegrationEventFaker().Generate();

        Assert.False(string.IsNullOrWhiteSpace(generated.Payload));
    }
}
