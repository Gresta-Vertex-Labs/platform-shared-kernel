using SharedKernel.Testing.Messaging;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Messaging;

public sealed class InMemoryMessageBusTests
{
    private sealed record TestCommand(string Value);

    private sealed record TestEvent(string Value);

    private sealed record TestRequest(int X);

    private sealed record TestResponse(int Y);

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
    public async Task RequestAsync_WithRegisteredHandler_ReturnsHandlerResult()
    {
        var bus = new InMemoryMessageBus();
        bus.SetResponseHandler<TestRequest, TestResponse>(req => new TestResponse(req.X * 2));

        var response = await bus.RequestAsync<TestRequest, TestResponse>(new TestRequest(21), CancellationToken.None);

        Assert.Equal(42, response.Y);
    }

    [Fact]
    public async Task RequestAsync_NoHandlerRegistered_ThrowsDescriptiveException()
    {
        var bus = new InMemoryMessageBus();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bus.RequestAsync<TestRequest, TestResponse>(new TestRequest(1), CancellationToken.None));

        Assert.Contains(nameof(TestRequest), ex.Message);
        Assert.Contains(nameof(TestResponse), ex.Message);
    }

    [Fact]
    public async Task ExecuteRoutingSlipAsync_AlwaysThrowsNotSupported()
    {
        var bus = new InMemoryMessageBus();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            bus.ExecuteRoutingSlipAsync(new object(), CancellationToken.None));
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
}
