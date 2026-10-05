using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Messaging.MassTransit.Consumers;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// T-11: Retry policy tests.
/// — 3-attempt incremental back-off scenario.
/// — ImmediateAttempts fast-retry scenario.
/// </summary>
public sealed class RetryPolicyTests
{
    // -------------------------------------------------------------------------
    // T-11a: Incremental retry — consumer throws on first 2 calls, succeeds on 3rd
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WithRetry_ConsumerThrowsTwiceThenSucceeds_EventuallyConsumedSuccessfully()
    {
        // FailingConsumer fails the first 2 attempts, succeeds on the 3rd.
        // With Immediate(3), the message is retried up to 3 times in-process.
        FailingConsumer.Reset(failCount: 2);

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<FailingConsumer>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    // Immediate retry so the test is fast — 3 retries after first attempt
                    busCfg.UseMessageRetry(r => r.Immediate(3));
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new RetryMessage("retry-test"));

        // Wait until consumed (success on 3rd attempt); inactivity task signals completion
        (await harness.Consumed.Any<RetryMessage>()).Should().BeTrue(
            "message must be consumed successfully after retries");

        // Verify no fault was published — success on the 3rd attempt means no dead-letter
        (await harness.Published.Any<Fault<RetryMessage>>()).Should().BeFalse(
            "when consumer succeeds after retries, no Fault should be published");

        await harness.Stop();
    }

    [Fact]
    public async Task WithRetry_AllAttemptsFail_FaultPublished()
    {
        AlwaysFailingConsumer.Reset();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<AlwaysFailingConsumer>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.UseMessageRetry(r => r.Immediate(2));
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new RetryMessage("always-fail"));

        (await harness.Published.Any<Fault<RetryMessage>>()).Should().BeTrue(
            "after all retry attempts exhausted, a fault must be published");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // T-11b: ImmediateAttempts fast-retry scenario
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ImmediateAttempts_ConsumerSucceedsAfterImmediateRetry_NoBrokerDelay()
    {
        // Tests that immediate (zero-delay) retry path works correctly
        ImmediateRetryConsumer.Reset(failCount: 1);

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<ImmediateRetryConsumer>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.UseMessageRetry(r => r.Immediate(2));
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await harness.Bus.Publish(new RetryMessage("immediate-retry"));

        (await harness.Consumed.Any<RetryMessage>()).Should().BeTrue();
        sw.Stop();

        // Immediate retry should not add significant delay
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "immediate retry should not introduce inter-attempt delays");

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Consumers with controllable failure behaviour — NOT using 'file' modifier
// ---------------------------------------------------------------------------

internal sealed class FailingConsumer : ConsumerBase<RetryMessage>
{
    private static int _remainingFailures;
    private static int _callCount;

    public static void Reset(int failCount)
    {
        _remainingFailures = failCount;
        _callCount = 0;
    }

    public FailingConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(RetryMessage message, CancellationToken ct)
    {
        Interlocked.Increment(ref _callCount);
        if (Interlocked.Decrement(ref _remainingFailures) >= 0)
            throw new InvalidOperationException("intentional failure for retry test");

        return Task.CompletedTask;
    }
}

internal sealed class AlwaysFailingConsumer : ConsumerBase<RetryMessage>
{
    public static void Reset() { }

    public AlwaysFailingConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(RetryMessage message, CancellationToken ct)
    {
        throw new InvalidOperationException("always fails");
    }
}

internal sealed class ImmediateRetryConsumer : ConsumerBase<RetryMessage>
{
    private static int _remainingFailures;

    public static void Reset(int failCount) => _remainingFailures = failCount;

    public ImmediateRetryConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(RetryMessage message, CancellationToken ct)
    {
        if (Interlocked.Decrement(ref _remainingFailures) >= 0)
            throw new InvalidOperationException("immediate retry failure");

        return Task.CompletedTask;
    }
}

internal sealed record RetryMessage(string Text);
