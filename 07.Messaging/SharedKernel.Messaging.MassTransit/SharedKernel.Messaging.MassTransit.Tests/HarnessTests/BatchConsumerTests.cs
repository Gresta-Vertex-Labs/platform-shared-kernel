#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Messaging.MassTransit.Consumers;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// B-04 / B-05 / B-06: Batch consumer TestHarness tests.
/// </summary>
public sealed class BatchConsumerTests
{
    // -------------------------------------------------------------------------
    // B-04: Batch delivery — 5 messages with MessageLimit=5 → exactly one ConsumeAsync call
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Consume_WhenMessageLimitReached_InvokesConsumeAsyncOnce()
    {
        BatchAggregatingConsumer.Reset();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<BatchAggregatingConsumer>(configurator =>
                    configurator.Options<global::MassTransit.BatchOptions>(o =>
                    {
                        o.MessageLimit = 5;
                        o.TimeLimit = TimeSpan.FromSeconds(30); // large — rely on MessageLimit
                        o.ConcurrencyLimit = 1;
                    }));
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Publish 5 messages — triggers delivery when MessageLimit is reached.
        for (var i = 0; i < 5; i++)
            await harness.Bus.Publish(new BatchTestMessage($"msg-{i}"));

        // Wait for the batch consumer to be called.
        await harness.InactivityTask;

        BatchAggregatingConsumer.InvocationCount.Should().Be(1,
            "ConsumeAsync must be called exactly once when all 5 messages form a single batch");
        BatchAggregatingConsumer.LastBatchSize.Should().Be(5,
            "the batch delivered to ConsumeAsync must contain all 5 published messages");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // B-05: Time-limit test — 2 messages with MessageLimit=10, TimeLimit=100ms
    //        batch should arrive as a partial batch after TimeLimit elapses
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Consume_WhenTimeLimitElapses_DeliversPartialBatch()
    {
        BatchTimeLimitConsumer.Reset();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<BatchTimeLimitConsumer>(configurator =>
                    configurator.Options<global::MassTransit.BatchOptions>(o =>
                    {
                        o.MessageLimit = 10;           // won't be reached
                        o.TimeLimit = TimeSpan.FromMilliseconds(100);
                        o.ConcurrencyLimit = 1;
                    }));
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Publish only 2 messages — won't reach MessageLimit=10.
        await harness.Bus.Publish(new BatchTimeLimitMessage("a"));
        await harness.Bus.Publish(new BatchTimeLimitMessage("b"));

        // Wait for the partial batch to be delivered after the TimeLimit elapses.
        // InactivityTask waits until all bus activity completes.
        await harness.InactivityTask;

        BatchTimeLimitConsumer.InvocationCount.Should().Be(1,
            "partial batch must be delivered as one ConsumeAsync call after TimeLimit elapses");
        BatchTimeLimitConsumer.LastBatchSize.Should().Be(2,
            "the partial batch must contain exactly the 2 published messages");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // B-06: Exception propagation — ConsumeAsync throws; fault published for batch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Consume_WhenConsumeAsyncThrows_FaultIsPublishedForBatch()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<BatchThrowingConsumer>(configurator =>
                    configurator.Options<global::MassTransit.BatchOptions>(o =>
                    {
                        o.MessageLimit = 1;
                        o.TimeLimit = TimeSpan.FromMilliseconds(500);
                        o.ConcurrencyLimit = 1;
                    }));
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new BatchThrowingMessage("fail-batch"));

        // Wait for fault publication.
        await harness.InactivityTask;

        // MassTransit 9.x publishes Fault<TMessage> (the unwrapped individual message type)
        // when a batch consumer throws — not Fault<Batch<TMessage>>.
        (await harness.Published.Any<Fault<BatchThrowingMessage>>()).Should().BeTrue(
            "BatchConsumerBase must rethrow exceptions, triggering MassTransit fault publication");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // B-06b: Exception propagation — fault contains the original exception
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Consume_WhenConsumeAsyncThrows_FaultContainsException()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<BatchThrowingConsumer>(configurator =>
                    configurator.Options<global::MassTransit.BatchOptions>(o =>
                    {
                        o.MessageLimit = 1;
                        o.TimeLimit = TimeSpan.FromMilliseconds(500);
                        o.ConcurrencyLimit = 1;
                    }));
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new BatchThrowingMessage("exception-check"));

        await harness.InactivityTask;

        // MassTransit 9.x publishes Fault<TMessage> (the unwrapped individual message type).
        (await harness.Published.Any<Fault<BatchThrowingMessage>>()).Should().BeTrue();

        var fault = harness.Published.Select<Fault<BatchThrowingMessage>>().First();
        fault.Context.Message.Exceptions.Should().NotBeEmpty();
        fault.Context.Message.Exceptions.First().Message.Should().Contain("intentional batch exception");

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Concrete batch consumers — NOT using 'file' modifier so MassTransit harness
// type matching works correctly. 'file' types get mangled CLR names.
// ---------------------------------------------------------------------------

/// <summary>Records batch size and invocation count for B-04 assertions.</summary>
internal sealed class BatchAggregatingConsumer : BatchConsumerBase<BatchTestMessage>
{
    public static int InvocationCount;
    public static int LastBatchSize;

    public static void Reset() { InvocationCount = 0; LastBatchSize = 0; }

    public BatchAggregatingConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(IReadOnlyList<BatchTestMessage> messages, CancellationToken ct)
    {
        System.Threading.Interlocked.Increment(ref InvocationCount);
        System.Threading.Volatile.Write(ref LastBatchSize, messages.Count);
        return Task.CompletedTask;
    }
}

/// <summary>Records batch size and invocation count for B-05 assertions.</summary>
internal sealed class BatchTimeLimitConsumer : BatchConsumerBase<BatchTimeLimitMessage>
{
    public static int InvocationCount;
    public static int LastBatchSize;

    public static void Reset() { InvocationCount = 0; LastBatchSize = 0; }

    public BatchTimeLimitConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(IReadOnlyList<BatchTimeLimitMessage> messages, CancellationToken ct)
    {
        System.Threading.Interlocked.Increment(ref InvocationCount);
        System.Threading.Volatile.Write(ref LastBatchSize, messages.Count);
        return Task.CompletedTask;
    }
}

/// <summary>Always throws — for B-06 exception propagation assertions.</summary>
internal sealed class BatchThrowingConsumer : BatchConsumerBase<BatchThrowingMessage>
{
    public BatchThrowingConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(IReadOnlyList<BatchThrowingMessage> messages, CancellationToken ct)
        => throw new InvalidOperationException("intentional batch exception");
}

// ---------------------------------------------------------------------------
// Message types — not 'file' for the same reason as above
// ---------------------------------------------------------------------------

internal sealed record BatchTestMessage(string Text);
internal sealed record BatchTimeLimitMessage(string Text);
internal sealed record BatchThrowingMessage(string Text);
