#pragma warning disable CS8602 // MassTransit harness nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Messaging.Abstractions.HeaderPropagation;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;

// Alias to avoid ambiguity with MassTransit.PublishContext in test types.
using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// HP-06: Two propagators registered; both headers present; explicit callback overrides propagator key.
/// HP-07: Consumer log scope contains x-sk-* headers; non-x-sk-* headers are NOT added to scope.
/// </summary>
public sealed class HeaderPropagationTests
{
    // -------------------------------------------------------------------------
    // HP-06: Two propagators; explicit callback wins on conflict
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TwoPropagators_BothHeadersPresent_ExplicitCallbackWins()
    {
        // Arrange
        HpHeaderCaptureStore.Reset();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<HpHeaderCapturingConsumer>();
                cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
            })
            // Register two propagators (additive, in registration order).
            .AddScoped<IMessageHeaderPropagator, HpPropagatorA>()
            .AddScoped<IMessageHeaderPropagator, HpPropagatorB>()
            .AddSingleton<IReadOnlyDictionary<Type, string>>(
                new System.Collections.ObjectModel.ReadOnlyDictionary<Type, string>(new Dictionary<Type, string>()))
            .AddScoped<ConventionSendEndpointResolver>()
            .AddScoped<Abstractions.MessageBus.IMessageBus, MassTransitMessageBus>()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Resolve IMessageBus from a scope so propagators are also scoped correctly.
        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<Abstractions.MessageBus.IMessageBus>();

        // Act: publish with an explicit callback that overrides the key set by PropagatorA.
        await bus.PublishAsync(
            new HpPropagationTestMessage("test"),
            ctx =>
            {
                // PropagatorA sets "x-sk-header-a" = "from-propagator-a"
                // Explicit callback overrides it with "from-explicit".
                ctx.WithHeader("x-sk-header-a", "from-explicit");
            },
            CancellationToken.None);

        // Wait for consumer to process the message.
        (await harness.Consumed.Any<HpPropagationTestMessage>()).Should().BeTrue();

        // Assert: header-b (set only by PropagatorB) is present.
        HpHeaderCaptureStore.CapturedHeaders.Should().ContainKey("x-sk-header-b",
            "PropagatorB must have set x-sk-header-b");
        HpHeaderCaptureStore.CapturedHeaders["x-sk-header-b"].Should().Be("from-propagator-b");

        // Assert: header-a was overridden by the explicit callback (explicit wins).
        HpHeaderCaptureStore.CapturedHeaders.Should().ContainKey("x-sk-header-a",
            "x-sk-header-a must be present (set by either propagator or explicit callback)");
        HpHeaderCaptureStore.CapturedHeaders["x-sk-header-a"].Should().Be("from-explicit",
            "explicit callback must win over propagator when the same key is set");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // HP-07: Consumer log scope contains x-sk-* headers; non-x-sk-* headers NOT in scope
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Consumer_XSkHeaders_AddedToLogScope_NonXSkHeaders_NotAdded()
    {
        // Arrange: use a capturing test logger that records BeginScope calls.
        var capturingLogger = new HpCapturingLogger();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<HpLogScopeCapturingConsumer>();
                cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
            })
            .AddSingleton<HpCapturingLogger>(capturingLogger)
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        HpLogScopeCaptureStore.Reset();

        // Act: publish message with an x-sk-* header and a non-x-sk-* header.
        await harness.Bus.Publish(new HpLogScopeTestMessage("log-scope-test"), pipe =>
        {
            pipe.Headers.Set("x-sk-tenant-id", "tenant-abc");
            pipe.Headers.Set("custom-header", "should-not-appear"); // non-x-sk-
        });

        // Wait for consumer.
        (await harness.Consumed.Any<HpLogScopeTestMessage>()).Should().BeTrue();

        // Assert: x-sk-tenant-id is in the captured scope.
        HpLogScopeCaptureStore.CapturedXSkKeys.Should().Contain("x-sk-tenant-id",
            "x-sk-tenant-id header must be added to the structured log scope");

        // Assert: custom-header is NOT in the captured scope.
        HpLogScopeCaptureStore.CapturedXSkKeys.Should().NotContain("custom-header",
            "headers without x-sk- prefix must NOT be added to the log scope");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // Additional: no propagators — existing behavior unchanged
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NoPropagators_PublishAsync_MessageDelivered()
    {
        // Arrange: no IMessageHeaderPropagator registered.
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<HpHeaderCapturingConsumer>();
                cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
            })
            .AddSingleton<IReadOnlyDictionary<Type, string>>(
                new System.Collections.ObjectModel.ReadOnlyDictionary<Type, string>(new Dictionary<Type, string>()))
            .AddScoped<ConventionSendEndpointResolver>()
            .AddScoped<Abstractions.MessageBus.IMessageBus, MassTransitMessageBus>()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<Abstractions.MessageBus.IMessageBus>();

        // Act: publish with no propagators and no configure callback.
        await bus.PublishAsync(new HpPropagationTestMessage("no-propagators"), CancellationToken.None);

        // Assert: message is delivered.
        (await harness.Consumed.Any<HpPropagationTestMessage>()).Should().BeTrue();

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Message types — internal, no 'file' modifier
// ---------------------------------------------------------------------------

internal sealed record HpPropagationTestMessage(string Text);
internal sealed record HpLogScopeTestMessage(string Text);

// ---------------------------------------------------------------------------
// Static stores for cross-scope state capture
// ---------------------------------------------------------------------------

internal static class HpHeaderCaptureStore
{
    private static Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyDictionary<string, string> CapturedHeaders => _headers;
    public static void Capture(string key, string value) => _headers[key] = value;
    public static void Reset() => _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

internal static class HpLogScopeCaptureStore
{
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> _xSkKeys = [];
    public static IEnumerable<string> CapturedXSkKeys => _xSkKeys;
    public static void AddKey(string key) => _xSkKeys.Add(key);
    public static void Reset()
    {
        while (_xSkKeys.TryTake(out _)) { }
    }
}

// ---------------------------------------------------------------------------
// Propagators — set distinct header keys
// ---------------------------------------------------------------------------

internal sealed class HpPropagatorA : IMessageHeaderPropagator
{
    public void Propagate(MessagingPublishContext context)
        => context.WithHeader("x-sk-header-a", "from-propagator-a");
}

internal sealed class HpPropagatorB : IMessageHeaderPropagator
{
    public void Propagate(MessagingPublishContext context)
        => context.WithHeader("x-sk-header-b", "from-propagator-b");
}

// ---------------------------------------------------------------------------
// Consumers
// ---------------------------------------------------------------------------

/// Captures headers from ConsumeContext into HpHeaderCaptureStore for assertion.
internal sealed class HpHeaderCapturingConsumer : IConsumer<HpPropagationTestMessage>
{
    public Task Consume(ConsumeContext<HpPropagationTestMessage> context)
    {
        foreach (var header in context.Headers.GetAll())
        {
            if (header.Value is not null)
                HpHeaderCaptureStore.Capture(header.Key, header.Value.ToString() ?? string.Empty);
        }
        return Task.CompletedTask;
    }
}

/// Uses a capturing logger to verify that ConsumerBase.Consume adds x-sk-* headers to log scope.
internal sealed class HpLogScopeCapturingConsumer : ConsumerBase<HpLogScopeTestMessage>
{
    public HpLogScopeCapturingConsumer() : base(new HpCapturingLogger()) { }

    protected override Task ConsumeAsync(HpLogScopeTestMessage message, CancellationToken ct)
        => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// Logger that captures BeginScope keys for assertions
// ---------------------------------------------------------------------------

internal sealed class HpCapturingLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        if (state is IDictionary<string, object?> dict)
        {
            foreach (var key in dict.Keys)
                HpLogScopeCaptureStore.AddKey(key);
        }
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter) { }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
