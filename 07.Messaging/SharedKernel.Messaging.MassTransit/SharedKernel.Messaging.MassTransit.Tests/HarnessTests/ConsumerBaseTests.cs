#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Messaging.MassTransit.Consumers;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// T-07: ConsumerBase&lt;TMessage&gt; TestHarness tests.
/// — correct message delivery; exception propagation without swallowing; fault published on exception.
/// </summary>
public sealed class ConsumerBaseTests
{
    // -------------------------------------------------------------------------
    // T-07a: Correct message delivery
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Consume_PublishedMessage_InvokesConsumeAsync()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<RecordingConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var bus = harness.Bus;
        var message = new SimpleMessage("hello-world");
        await bus.Publish(message);

        // Wait for consumer to process
        (await harness.Consumed.Any<SimpleMessage>()).Should().BeTrue();

        var consumerHarness = harness.GetConsumerHarness<RecordingConsumer>();
        (await consumerHarness.Consumed.Any<SimpleMessage>()).Should().BeTrue();

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_MessagePayload_ReceivedCorrectly()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<RecordingConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var message = new SimpleMessage("check-payload");
        await harness.Bus.Publish(message);

        (await harness.Consumed.Any<SimpleMessage>()).Should().BeTrue();

        var consumed = harness.Consumed.Select<SimpleMessage>().ToList();
        consumed.Should().ContainSingle(m => m.Context.Message.Text == "check-payload");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // T-07b: Exception propagation without swallowing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Consume_WhenConsumeAsyncThrows_FaultIsPublished()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<ThrowingConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ThrowingMessage("fail-me"));

        // The fault envelope is published when ConsumerBase rethrows the exception
        (await harness.Published.Any<Fault<ThrowingMessage>>()).Should().BeTrue(
            "ConsumerBase must rethrow exceptions, triggering MassTransit fault publication");

        await harness.Stop();
    }

    [Fact]
    public async Task Consume_WhenConsumeAsyncThrows_FaultContainsException()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<ThrowingConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ThrowingMessage("exception-check"));

        (await harness.Published.Any<Fault<ThrowingMessage>>()).Should().BeTrue();

        var fault = harness.Published.Select<Fault<ThrowingMessage>>().First();
        fault.Context.Message.Exceptions.Should().NotBeEmpty();
        fault.Context.Message.Exceptions.First().Message.Should().Contain("intentional");

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Concrete consumers for testing — NOT using 'file' modifier so MassTransit
// harness type matching works correctly. 'file' types get mangled CLR names.
// ---------------------------------------------------------------------------

internal sealed class RecordingConsumer : ConsumerBase<SimpleMessage>
{
    public RecordingConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(SimpleMessage message, CancellationToken ct)
    {
        // No-op — just record receipt
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingConsumer : ConsumerBase<ThrowingMessage>
{
    public ThrowingConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(ThrowingMessage message, CancellationToken ct)
    {
        throw new InvalidOperationException("intentional test exception");
    }
}

// ---------------------------------------------------------------------------
// Message types — also not 'file' for the same reason
// ---------------------------------------------------------------------------

internal sealed record SimpleMessage(string Text);

internal sealed record ThrowingMessage(string Text);
