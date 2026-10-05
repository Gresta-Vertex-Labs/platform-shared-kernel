#pragma warning disable CS8602 // MassTransit harness nullable context
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Messaging.Abstractions.SchemaEvolution;
using SharedKernel.Messaging.MassTransit.SchemaEvolution;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// VT-04: Publish TOld; consumer registered for TNew; WithVersionTranslator wired via the
///        translating consumer; assert the TNew consumer receives correctly translated values.
/// VT-05: Translator registered with no consumer for TNew; advisory validation does not throw;
///        assert a Warning is logged.
/// VT-06: Translate() is called exactly once per message; not called for unrelated message types.
/// </summary>
public sealed class VersionTranslationTests
{
    // -------------------------------------------------------------------------
    // VT-04: TOld published, translated, and delivered to the TNew consumer
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublishOldSchema_TranslatedToNewSchema_ConsumerReceivesTranslatedValues()
    {
        VtNewSchemaConsumerTracker.Reset();

        await using var provider = new ServiceCollection()
            .AddSingleton<IMessageVersionTranslator<VtOrderPlacedV1, VtOrderPlacedV2>, VtOrderPlacedTranslator>()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<VersionTranslatingConsumer<VtOrderPlacedV1, VtOrderPlacedV2>>();
                cfg.AddConsumer<VtOrderPlacedV2Consumer>();
                cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Act: publish the legacy schema message.
        await harness.Bus.Publish(new VtOrderPlacedV1("order-123", 19.99m));

        // Assert: the TNew consumer receives the translated message with correct field values.
        (await harness.Consumed.Any<VtOrderPlacedV2>()).Should().BeTrue(
            "the translated VtOrderPlacedV2 message must be delivered to the new consumer");

        VtNewSchemaConsumerTracker.ReceivedMessages.Should().ContainSingle();
        var received = VtNewSchemaConsumerTracker.ReceivedMessages.Single();
        received.OrderId.Should().Be("order-123");
        received.TotalAmount.Should().Be(19.99m);
        received.Currency.Should().Be("USD", "the translator must default Currency to USD for V1 messages");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // VT-05: No consumer for TNew — advisory warning logged, Build() does not throw
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_NoConsumerForNewSchema_LogsWarning_DoesNotThrow()
    {
        var capturingLogger = new VtCapturingLogger();

        var act = () => TranslatorRegistrationValidator.Validate(
            typeof(VtOrderPlacedV1),
            typeof(VtOrderPlacedV2),
            registeredConsumerTypes: [], // no consumer for VtOrderPlacedV2 registered
            capturingLogger);

        act.Should().NotThrow("validation is advisory only and must never throw");

        capturingLogger.WarningLogged.Should().BeTrue(
            "a Warning must be logged when no consumer for the new schema type is registered");
    }

    [Fact]
    public async Task HostedService_NoConsumerForNewSchema_StartAsync_LogsWarning_DoesNotThrow()
    {
        var capturingLogger = new VtCapturingLoggerOf<TranslatorRegistrationValidationHostedService>();

        var hostedService = new TranslatorRegistrationValidationHostedService(
            typePairs: [(typeof(VtOrderPlacedV1), typeof(VtOrderPlacedV2))],
            registeredConsumerTypes: [], // no consumer for VtOrderPlacedV2
            capturingLogger);

        var act = async () => await hostedService.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync("the hosted service is advisory only and must never throw");

        capturingLogger.WarningLogged.Should().BeTrue(
            "the hosted service must log a Warning when no consumer for the new schema type is registered");
    }

    [Fact]
    public void Validate_ConsumerForNewSchemaRegistered_NoWarningLogged()
    {
        var capturingLogger = new VtCapturingLogger();

        TranslatorRegistrationValidator.Validate(
            typeof(VtOrderPlacedV1),
            typeof(VtOrderPlacedV2),
            registeredConsumerTypes: [typeof(VtOrderPlacedV2Consumer)],
            capturingLogger);

        capturingLogger.WarningLogged.Should().BeFalse(
            "no warning should be logged when a consumer for the new schema type is registered");
    }

    // -------------------------------------------------------------------------
    // VT-06: Translate() called exactly once per message; not called for unrelated types
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Translate_CalledExactlyOncePerMessage_NotCalledForUnrelatedMessages()
    {
        VtCountingTranslator.Reset();
        VtNewSchemaConsumerTracker.Reset();
        VtUnrelatedConsumerTracker.Reset();

        await using var provider = new ServiceCollection()
            .AddSingleton<IMessageVersionTranslator<VtOrderPlacedV1, VtOrderPlacedV2>, VtCountingTranslator>()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<VersionTranslatingConsumer<VtOrderPlacedV1, VtOrderPlacedV2>>();
                cfg.AddConsumer<VtOrderPlacedV2Consumer>();
                cfg.AddConsumer<VtUnrelatedConsumer>();
                cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // Act: publish one TOld message and one unrelated message.
        await harness.Bus.Publish(new VtOrderPlacedV1("order-456", 5.00m));
        await harness.Bus.Publish(new VtUnrelatedMessage("unrelated"));

        (await harness.Consumed.Any<VtOrderPlacedV2>()).Should().BeTrue();
        (await harness.Consumed.Any<VtUnrelatedMessage>()).Should().BeTrue();

        // Assert: Translate() called exactly once.
        VtCountingTranslator.CallCount.Should().Be(1,
            "Translate() must be called exactly once for the single TOld message");

        // Assert: unrelated message was consumed but did not trigger translation.
        VtUnrelatedConsumerTracker.ReceivedMessages.Should().ContainSingle();

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Message types — internal, no 'file' modifier (MassTransit type matching)
// ---------------------------------------------------------------------------

/// <summary>Legacy (V1) schema — no Currency field.</summary>
internal sealed record VtOrderPlacedV1(string OrderId, decimal TotalAmount);

/// <summary>Current (V2) schema — adds a Currency field.</summary>
internal sealed record VtOrderPlacedV2(string OrderId, decimal TotalAmount, string Currency);

/// <summary>An unrelated message type — must not be affected by version translation.</summary>
internal sealed record VtUnrelatedMessage(string Text);

// ---------------------------------------------------------------------------
// Translators
// ---------------------------------------------------------------------------

/// <summary>Pure projection from VtOrderPlacedV1 to VtOrderPlacedV2, defaulting Currency to USD.</summary>
internal sealed class VtOrderPlacedTranslator : IMessageVersionTranslator<VtOrderPlacedV1, VtOrderPlacedV2>
{
    public VtOrderPlacedV2 Translate(VtOrderPlacedV1 old) =>
        new(old.OrderId, old.TotalAmount, "USD");
}

/// <summary>Same projection as <see cref="VtOrderPlacedTranslator"/>, but counts invocations.</summary>
internal sealed class VtCountingTranslator : IMessageVersionTranslator<VtOrderPlacedV1, VtOrderPlacedV2>
{
    private static int _callCount;

    public static int CallCount => _callCount;

    public static void Reset() => Interlocked.Exchange(ref _callCount, 0);

    public VtOrderPlacedV2 Translate(VtOrderPlacedV1 old)
    {
        Interlocked.Increment(ref _callCount);
        return new VtOrderPlacedV2(old.OrderId, old.TotalAmount, "USD");
    }
}

// ---------------------------------------------------------------------------
// Trackers
// ---------------------------------------------------------------------------

internal static class VtNewSchemaConsumerTracker
{
    private static readonly List<VtOrderPlacedV2> Messages = [];
    private static readonly Lock SyncRoot = new();

    public static IReadOnlyList<VtOrderPlacedV2> ReceivedMessages
    {
        get
        {
            lock (SyncRoot)
                return [.. Messages];
        }
    }

    public static void Add(VtOrderPlacedV2 message)
    {
        lock (SyncRoot)
            Messages.Add(message);
    }

    public static void Reset()
    {
        lock (SyncRoot)
            Messages.Clear();
    }
}

internal static class VtUnrelatedConsumerTracker
{
    private static readonly List<VtUnrelatedMessage> Messages = [];
    private static readonly Lock SyncRoot = new();

    public static IReadOnlyList<VtUnrelatedMessage> ReceivedMessages
    {
        get
        {
            lock (SyncRoot)
                return [.. Messages];
        }
    }

    public static void Add(VtUnrelatedMessage message)
    {
        lock (SyncRoot)
            Messages.Add(message);
    }

    public static void Reset()
    {
        lock (SyncRoot)
            Messages.Clear();
    }
}

// ---------------------------------------------------------------------------
// Consumers — internal, no 'file' modifier
// ---------------------------------------------------------------------------

internal sealed class VtOrderPlacedV2Consumer : IConsumer<VtOrderPlacedV2>
{
    public Task Consume(ConsumeContext<VtOrderPlacedV2> context)
    {
        VtNewSchemaConsumerTracker.Add(context.Message);
        return Task.CompletedTask;
    }
}

internal sealed class VtUnrelatedConsumer : IConsumer<VtUnrelatedMessage>
{
    public Task Consume(ConsumeContext<VtUnrelatedMessage> context)
    {
        VtUnrelatedConsumerTracker.Add(context.Message);
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// Capturing loggers
// ---------------------------------------------------------------------------

/// <summary>Non-generic capturing logger that records whether a Warning was logged.</summary>
internal sealed class VtCapturingLogger : ILogger
{
    public bool WarningLogged { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
            WarningLogged = true;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>Generic capturing <see cref="ILogger{TCategoryName}"/> that records whether a Warning was logged.</summary>
internal sealed class VtCapturingLoggerOf<T> : ILogger<T>
{
    public bool WarningLogged { get; private set; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
            WarningLogged = true;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
