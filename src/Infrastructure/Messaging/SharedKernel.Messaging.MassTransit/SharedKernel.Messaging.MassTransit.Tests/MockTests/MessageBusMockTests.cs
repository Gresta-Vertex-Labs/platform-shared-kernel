using FluentAssertions;
using NSubstitute;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.MessageBus;

namespace SharedKernel.Messaging.MassTransit.Tests.MockTests;

/// <summary>
/// T-04: IMessageBus NSubstitute mock tests.
/// T-05: IEventPublisher NSubstitute mock tests.
/// Verifies application-layer handlers call the correct methods with expected arguments.
/// </summary>
public sealed class MessageBusMockTests
{
    // ---------------------------------------------------------------------------
    // T-04: IMessageBus
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_WithMessage_CallsPublishOnce()
    {
        var bus = Substitute.For<IMessageBus>();
        var message = new TestCommand("test-data");

        await bus.PublishAsync(message, CancellationToken.None);

        await bus.Received(1).PublishAsync(message, CancellationToken.None);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_CallsPublishOnce()
    {
        var bus = Substitute.For<IMessageBus>();
        var message = new TestCommand("with-configure");
        Action<PublishContext> configure = ctx => ctx.WithCorrelationId(Guid.NewGuid());

        await bus.PublishAsync(message, configure, CancellationToken.None);

        await bus.Received(1).PublishAsync(message, configure, CancellationToken.None);
    }

    [Fact]
    public async Task SendAsync_WithCommand_CallsSendOnce()
    {
        var bus = Substitute.For<IMessageBus>();
        var command = new TestCommand("send-me");

        await bus.SendAsync(command, CancellationToken.None);

        await bus.Received(1).SendAsync(command, CancellationToken.None);
    }

    [Fact]
    public async Task SendAsync_WithDifferentCommand_DoesNotCallPublish()
    {
        var bus = Substitute.For<IMessageBus>();
        var command = new TestCommand("check-isolation");

        await bus.SendAsync(command, CancellationToken.None);

        await bus.DidNotReceive().PublishAsync(command, CancellationToken.None);
    }

    [Fact]
    public async Task PublishAsync_CalledWithCorrectMessageType()
    {
        var bus = Substitute.For<IMessageBus>();
        var message = new AnotherTestCommand(42);

        await bus.PublishAsync(message, CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Is<AnotherTestCommand>(m => m.Value == 42), CancellationToken.None);
    }

    // ---------------------------------------------------------------------------
    // T-05: IEventPublisher
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_IntegrationEvent_CallsPublisherOnce()
    {
        var publisher = Substitute.For<IEventPublisher>();
        var evt = new TestIntegrationEvent("integration-payload");

        await publisher.PublishAsync(evt, CancellationToken.None);

        await publisher.Received(1).PublishAsync(evt, CancellationToken.None);
    }

    [Fact]
    public async Task PublishAsync_WithConfigure_IntegrationEvent_CallsPublisherOnce()
    {
        var publisher = Substitute.For<IEventPublisher>();
        var evt = new TestIntegrationEvent("with-ctx");
        Action<PublishContext> configure = ctx => ctx.WithCausationId(Guid.NewGuid());

        await publisher.PublishAsync(evt, configure, CancellationToken.None);

        await publisher.Received(1).PublishAsync(evt, configure, CancellationToken.None);
    }

    [Fact]
    public async Task PublishAsync_CalledWithCorrectEventType()
    {
        var publisher = Substitute.For<IEventPublisher>();
        var evt = new TestIntegrationEvent("payload-check");

        await publisher.PublishAsync(evt, CancellationToken.None);

        await publisher.Received(1).PublishAsync(
            Arg.Is<TestIntegrationEvent>(e => e.Payload == "payload-check"),
            CancellationToken.None);
    }
}

// ---------------------------------------------------------------------------
// Test message types — defined in this file to keep test fixtures self-contained.
// ---------------------------------------------------------------------------

file sealed record TestCommand(string Data);

file sealed record AnotherTestCommand(int Value);

[IntegrationEvent("tests.messaging.mock.integration-event")]
file sealed record TestIntegrationEvent(string Payload) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();

    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
