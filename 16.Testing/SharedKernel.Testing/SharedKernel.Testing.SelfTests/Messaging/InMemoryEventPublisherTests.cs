using SharedKernel.Testing.Messaging;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Messaging;

public sealed class InMemoryEventPublisherTests
{
    private sealed record TestIntegrationEvent(string Value);

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
}
