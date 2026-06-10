#pragma warning disable CS8602 // MassTransit harness nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Messaging.MassTransit.Consumers;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// CD-05: Subclass declares ValidationException in NonRetryableExceptions;
///        consumer throws ValidationException; assert no retry attempted (fault published immediately).
/// CD-06: Subclass returns empty NonRetryableExceptions;
///        consumer throws IOException; assert retry is attempted per global retry policy.
/// </summary>
public sealed class ConsumerDefinitionTests
{
    // -------------------------------------------------------------------------
    // CD-05: Non-retryable exception — fault published immediately, no retry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NonRetryableException_FaultPublishedImmediately_NoRetryAttempted()
    {
        // Arrange: register the consumer via ConsumerDefinitionBase subclass that declares
        // CdValidationException as non-retryable.
        // The definition configures endpoint-level Immediate(3) + Ignore(CdValidationException).
        // No global bus-level retry is configured here — the definition provides the only retry policy.
        // With the exception ignored, the fault is published after the first (and only) attempt.
        CdConsumerTracker.ResetNonRetryable();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<CdNonRetryableConsumer, CdNonRetryableDefinition>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    // No global retry — the definition provides the only retry policy with Ignore.
                    // This ensures the endpoint-level Ignore filter is the authoritative policy.
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Act: publish a message that causes the consumer to throw CdValidationException.
        await harness.Bus.Publish(new CdNonRetryableMessage("trigger-validation-error"));

        // Wait for the fault to be published.
        (await harness.Published.Any<Fault<CdNonRetryableMessage>>()).Should().BeTrue(
            "a fault must be published when the consumer throws a non-retryable exception");

        // Assert: consumer was called exactly once — CdValidationException bypasses retry.
        CdConsumerTracker.NonRetryableConsumeCount.Should().Be(1,
            "consumer must be invoked exactly once; non-retryable exceptions bypass retry");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // CD-06: Retryable exception — retry attempted per global policy
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RetryableException_GlobalRetryPolicyApplied_FaultNotPublishedUntilBudgetExhausted()
    {
        // Arrange: register the consumer via ConsumerDefinitionBase subclass that declares
        // empty NonRetryableExceptions. The global retry policy allows 2 immediate retries.
        // Consumer always throws IOException — retryable — so fault is published after exhaustion.
        CdConsumerTracker.ResetRetryable();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<CdRetryableConsumer, CdRetryableDefinition>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    // Global retry: 2 immediate retries (3 total attempts: 1 original + 2 retries).
                    busCfg.UseMessageRetry(r => r.Immediate(2));
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Act: publish a message that causes the consumer to always throw IOException.
        await harness.Bus.Publish(new CdRetryableMessage("trigger-io-error"));

        // Wait for fault after retries are exhausted.
        (await harness.Published.Any<Fault<CdRetryableMessage>>()).Should().BeTrue(
            "a fault must be published after the retry budget is exhausted");

        // Assert: consumer was called more than once — retries fired for IOException.
        CdConsumerTracker.RetryableConsumeCount.Should().BeGreaterThan(1,
            "consumer must be retried; IOException is retryable per the global policy");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // Additional: EndpointName and PrefetchCount are applied
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsumerDefinitionBase_MinimalSubclass_MessageDelivered()
    {
        // Arrange: subclass with all defaults (empty NonRetryableExceptions, null EndpointName,
        // null PrefetchCount) — behaves like a standard consumer.
        CdMinimalTracker.Reset();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<CdMinimalConsumer, CdMinimalDefinition>();
                cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new CdMinimalMessage("hello"));

        (await harness.Consumed.Any<CdMinimalMessage>()).Should().BeTrue(
            "consumer with default ConsumerDefinitionBase must receive the message");

        CdMinimalTracker.ConsumeCount.Should().Be(1);

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // Additional: ConfigureConsumer is called with IBusRegistrationContext
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsumerDefinitionBase_ConfigureConsumerCalled_WithNonNullContext()
    {
        // Arrange: subclass that records whether ConfigureConsumer was called.
        CdContextTracker.Reset();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<CdContextConsumer, CdContextDefinition>();
                cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new CdContextMessage("ctx-test"));

        (await harness.Consumed.Any<CdContextMessage>()).Should().BeTrue();

        // Assert: ConfigureConsumer was invoked (harness registers the definition).
        CdContextTracker.ConfigureConsumerCalled.Should().BeTrue(
            "ConfigureConsumer must be invoked by the sealed Configure entry point");

        CdContextTracker.ContextWasNonNull.Should().BeTrue(
            "IBusRegistrationContext passed to ConfigureConsumer must be non-null");

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Custom exception — simulates a validation failure
// ---------------------------------------------------------------------------

internal sealed class CdValidationException : Exception
{
    public CdValidationException(string message) : base(message) { }
}

// ---------------------------------------------------------------------------
// Message types — internal, no 'file' modifier (MassTransit type matching)
// ---------------------------------------------------------------------------

internal sealed record CdNonRetryableMessage(string Text);
internal sealed record CdRetryableMessage(string Text);
internal sealed record CdMinimalMessage(string Text);
internal sealed record CdContextMessage(string Text);

// ---------------------------------------------------------------------------
// Static trackers for cross-scope invocation counts
// ---------------------------------------------------------------------------

internal static class CdConsumerTracker
{
    private static int _nonRetryableCount;
    private static int _retryableCount;

    public static int NonRetryableConsumeCount => _nonRetryableCount;
    public static int RetryableConsumeCount => _retryableCount;

    public static void IncrementNonRetryable() => Interlocked.Increment(ref _nonRetryableCount);
    public static void IncrementRetryable() => Interlocked.Increment(ref _retryableCount);

    public static void ResetNonRetryable() => Interlocked.Exchange(ref _nonRetryableCount, 0);
    public static void ResetRetryable() => Interlocked.Exchange(ref _retryableCount, 0);
}

internal static class CdMinimalTracker
{
    private static int _count;
    public static int ConsumeCount => _count;
    public static void Increment() => Interlocked.Increment(ref _count);
    public static void Reset() => Interlocked.Exchange(ref _count, 0);
}

internal static class CdContextTracker
{
    public static bool ConfigureConsumerCalled { get; private set; }
    public static bool ContextWasNonNull { get; private set; }

    public static void SetConfigured(bool contextNonNull)
    {
        ConfigureConsumerCalled = true;
        ContextWasNonNull = contextNonNull;
    }

    public static void Reset()
    {
        ConfigureConsumerCalled = false;
        ContextWasNonNull = false;
    }
}

// ---------------------------------------------------------------------------
// Consumers — internal, no 'file' modifier
// ---------------------------------------------------------------------------

internal sealed class CdNonRetryableConsumer : ConsumerBase<CdNonRetryableMessage>
{
    public CdNonRetryableConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(CdNonRetryableMessage message, CancellationToken ct)
    {
        CdConsumerTracker.IncrementNonRetryable();
        throw new CdValidationException("simulated validation failure — must not be retried");
    }
}

internal sealed class CdRetryableConsumer : ConsumerBase<CdRetryableMessage>
{
    public CdRetryableConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(CdRetryableMessage message, CancellationToken ct)
    {
        CdConsumerTracker.IncrementRetryable();
        throw new System.IO.IOException("simulated IO failure — must be retried");
    }
}

internal sealed class CdMinimalConsumer : ConsumerBase<CdMinimalMessage>
{
    public CdMinimalConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(CdMinimalMessage message, CancellationToken ct)
    {
        CdMinimalTracker.Increment();
        return Task.CompletedTask;
    }
}

internal sealed class CdContextConsumer : ConsumerBase<CdContextMessage>
{
    public CdContextConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(CdContextMessage message, CancellationToken ct)
        => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// Consumer definitions — extend ConsumerDefinitionBase<TConsumer>
// ---------------------------------------------------------------------------

/// <summary>
/// Definition that declares CdValidationException as non-retryable.
/// </summary>
internal sealed class CdNonRetryableDefinition : ConsumerDefinitionBase<CdNonRetryableConsumer>
{
    protected override IReadOnlyList<Type> NonRetryableExceptions =>
        [typeof(CdValidationException)];

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<CdNonRetryableConsumer> consumerConfigurator,
        IBusRegistrationContext context)
    {
        // No additional configuration.
    }
}

/// <summary>
/// Definition with empty NonRetryableExceptions — all exceptions are retried per global policy.
/// </summary>
internal sealed class CdRetryableDefinition : ConsumerDefinitionBase<CdRetryableConsumer>
{
    // Empty list (default) — all exceptions go through global retry policy.
    protected override IReadOnlyList<Type> NonRetryableExceptions => [];

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<CdRetryableConsumer> consumerConfigurator,
        IBusRegistrationContext context)
    {
        // No additional configuration.
    }
}

/// <summary>
/// Minimal definition with all defaults — null EndpointName, null PrefetchCount, empty NonRetryable.
/// </summary>
internal sealed class CdMinimalDefinition : ConsumerDefinitionBase<CdMinimalConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<CdMinimalConsumer> consumerConfigurator,
        IBusRegistrationContext context)
    {
        // Nothing additional.
    }
}

/// <summary>
/// Definition that records when ConfigureConsumer is called.
/// </summary>
internal sealed class CdContextDefinition : ConsumerDefinitionBase<CdContextConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<CdContextConsumer> consumerConfigurator,
        IBusRegistrationContext context)
    {
        CdContextTracker.SetConfigured(context is not null);
    }
}
