#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using System.Collections.ObjectModel;
using System.Diagnostics;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Contracts.Events;
using SharedKernel.Domain.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.TenantContext;
using SharedKernel.Messaging.MassTransit.EventPublisher;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// SK.07.AmbientPropagation / P-345: <c>MessagingBusBuilder.WithAmbientCorrelationPropagation()</c>
/// and <c>MessagingBusBuilder.WithTenantContext&lt;TAccessor&gt;()</c> exercised through the real
/// builder API (not manual DI wiring), using <c>MassTransit.Testing.TestHarness</c> for message flow.
/// </summary>
public sealed class AmbientPropagationTests
{
    // -------------------------------------------------------------------------
    // AP-10: WithAmbientCorrelationPropagation() — all three IMessageBus dispatch verbs
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_WithAmbientCorrelationPropagation_PopulatesCorrelationIdFromActivityTraceId()
    {
        // Arrange
        ApPublishCorrelationCaptureStore.Reset();

        var services = new ServiceCollection();
        services.AddSharedKernelMessaging(o => o.ServiceName = "ap-publish-service")
            .WithAmbientCorrelationPropagation();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<ApPublishCapturingConsumer>();
            cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
        });
        services.AddSingleton<IReadOnlyDictionary<Type, string>>(
            new ReadOnlyDictionary<Type, string>(new Dictionary<Type, string>()));
        services.AddScoped<ConventionSendEndpointResolver>();
        services.AddScoped<IMessageBus, MassTransitMessageBus>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // Act: publish with an ambient Activity in scope.
        using var activity = new Activity("ap-publish-test").Start();
        var expectedCorrelationId = Guid.Parse(activity.TraceId.ToString());

        await bus.PublishAsync(new ApPublishTestMessage("payload"), CancellationToken.None);

        (await harness.Consumed.Any<ApPublishTestMessage>()).Should().BeTrue();

        // Assert
        ApPublishCorrelationCaptureStore.CapturedCorrelationId.Should().Be(expectedCorrelationId,
            "AmbientCorrelationHeaderPropagator must populate CorrelationId from Activity.Current.TraceId " +
            "on PublishAsync");

        await harness.Stop();
    }

    [Fact]
    public async Task SendAsync_WithAmbientCorrelationPropagation_PopulatesCorrelationIdFromActivityTraceId()
    {
        // Arrange
        ApSendCorrelationCaptureStore.Reset();

        const string queueName = "ap-send-correlation-queue";
        var routeMap = new Dictionary<Type, string> { [typeof(ApSendTestCommand)] = queueName };

        var services = new ServiceCollection();
        services.AddSharedKernelMessaging(o => o.ServiceName = "ap-send-service")
            .WithAmbientCorrelationPropagation();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<ApSendCapturingConsumer>();
            cfg.UsingInMemory((ctx, busCfg) =>
            {
                busCfg.ReceiveEndpoint(queueName, e => e.ConfigureConsumer<ApSendCapturingConsumer>(ctx));
            });
        });
        services.AddSingleton<IReadOnlyDictionary<Type, string>>(new ReadOnlyDictionary<Type, string>(routeMap));
        services.AddScoped<ConventionSendEndpointResolver>();
        services.AddScoped<IMessageBus, MassTransitMessageBus>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        // Act
        using var activity = new Activity("ap-send-test").Start();
        var expectedCorrelationId = Guid.Parse(activity.TraceId.ToString());

        await bus.SendAsync(new ApSendTestCommand("payload"), CancellationToken.None);

        (await harness.Consumed.Any<ApSendTestCommand>()).Should().BeTrue();

        // Assert
        ApSendCorrelationCaptureStore.CapturedCorrelationId.Should().Be(expectedCorrelationId,
            "AmbientCorrelationHeaderPropagator must populate CorrelationId from Activity.Current.TraceId " +
            "on SendAsync, identically to PublishAsync");

        await harness.Stop();
    }

    [Fact]
    public async Task RequestAsync_WithAmbientCorrelationPropagation_PopulatesCorrelationIdFromActivityTraceId()
    {
        // Arrange
        ApRequestCorrelationCaptureStore.Reset();

        var services = new ServiceCollection();
        services.AddSharedKernelMessaging(o => o.ServiceName = "ap-request-service")
            .WithAmbientCorrelationPropagation();

        services.AddMassTransitTestHarness(cfg =>
        {
            cfg.AddConsumer<ApRequestRespondingConsumer>();
            cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
        });
        services.AddSingleton<IReadOnlyDictionary<Type, string>>(
            new ReadOnlyDictionary<Type, string>(new Dictionary<Type, string>()));
        services.AddScoped<ConventionSendEndpointResolver>();
        services.AddScoped<IMessageBus, MassTransitMessageBus>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using var scope = provider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        using var activity = new Activity("ap-request-test").Start();
        var expectedCorrelationId = Guid.Parse(activity.TraceId.ToString());

        var response = await bus.RequestAsync<ApRequestMessage, ApResponseMessage>(
            new ApRequestMessage("ping"),
            cts.Token);

        // Assert
        response.Should().NotBeNull();
        ApRequestCorrelationCaptureStore.CapturedCorrelationId.Should().Be(expectedCorrelationId,
            "AmbientCorrelationHeaderPropagator must populate CorrelationId from Activity.Current.TraceId " +
            "on RequestAsync, identically to PublishAsync/SendAsync");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // AP-11 / AP-12: WithTenantContext<TAccessor>() — envelope TenantId populated / no-op
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_WithTenantContextRegistered_PopulatesEnvelopeTenantIdFromAccessor()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelMessaging(o => o.ServiceName = "ap-tenant-service")
            .WithTenantContext<ApFakeTenantContextAccessor>();

        services.AddMassTransitTestHarness();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new ApIntegrationTestEvent { OccurredOn = DateTimeOffset.UtcNow };

        await publisher.PublishAsync(evt, CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<ApIntegrationTestEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<ApIntegrationTestEvent>>().First();

        envelope.Context.Message.TenantId.Should().Be(ApFakeTenantContextAccessor.FixedTenantId,
            "TenantHeaderPropagator must populate PublishContext.TenantId from the registered " +
            "ITenantContextAccessor, flowing through into EventEnvelope<TEvent>.TenantId");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_WithoutTenantContextRegistered_EnvelopeTenantIdIsNull_NoException()
    {
        var services = new ServiceCollection();

        // WithTenantContext<T>() deliberately NOT called — ITenantContextAccessor is never registered.
        services.AddSharedKernelMessaging(o => o.ServiceName = "ap-no-tenant-service");

        services.AddMassTransitTestHarness();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new ApIntegrationTestEvent { OccurredOn = DateTimeOffset.UtcNow };

        // Act: must not throw, even though no ITenantContextAccessor/propagator is registered.
        var act = async () => await publisher.PublishAsync(evt, CancellationToken.None);
        await act.Should().NotThrowAsync();

        (await harness.Published.Any<EventEnvelope<ApIntegrationTestEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<ApIntegrationTestEvent>>().First();

        envelope.Context.Message.TenantId.Should().BeNull(
            "with no ITenantContextAccessor registered, TenantHeaderPropagator is never registered " +
            "either (WithTenantContext<T>() was not called), so TenantId stays unset — a provable no-op");

        await harness.Stop();
    }

    // -------------------------------------------------------------------------
    // AP-13: explicit Action<PublishContext> callback overrides both new propagators
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_ExplicitCallback_OverridesBothAmbientPropagators()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelMessaging(o => o.ServiceName = "ap-override-service")
            .WithAmbientCorrelationPropagation()
            .WithTenantContext<ApFakeTenantContextAccessor>();

        services.AddMassTransitTestHarness();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new ApIntegrationTestEvent { OccurredOn = DateTimeOffset.UtcNow };

        var explicitCorrelationId = Guid.NewGuid();
        var explicitTenantId = Guid.NewGuid();

        // Act: an active Activity would otherwise drive CorrelationId, and the registered accessor
        // would otherwise drive TenantId — the explicit callback must win on both.
        using var activity = new Activity("ap-override-test").Start();

        await publisher.PublishAsync(
            evt,
            ctx => ctx.WithCorrelationId(explicitCorrelationId).WithTenantId(explicitTenantId),
            CancellationToken.None);

        (await harness.Published.Any<EventEnvelope<ApIntegrationTestEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<ApIntegrationTestEvent>>().First();

        envelope.Context.Message.CorrelationId.Should().Be(explicitCorrelationId.ToString("D"),
            "an explicit WithCorrelationId callback must win over AmbientCorrelationHeaderPropagator");
        envelope.Context.Message.TenantId.Should().Be(explicitTenantId,
            "an explicit WithTenantId callback must win over TenantHeaderPropagator");
        envelope.Context.Message.TenantId.Should().NotBe(ApFakeTenantContextAccessor.FixedTenantId);

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Message types — internal, no 'file' modifier (breaks MassTransit type matching)
// ---------------------------------------------------------------------------

internal sealed record ApPublishTestMessage(string Text);
internal sealed record ApSendTestCommand(string Text);
internal sealed record ApRequestMessage(string Text);
internal sealed record ApResponseMessage(string Reply);

internal sealed record ApIntegrationTestEvent : DomainEvent;

// ---------------------------------------------------------------------------
// Static stores for cross-scope state capture
// ---------------------------------------------------------------------------

internal static class ApPublishCorrelationCaptureStore
{
    private static Guid? _capturedCorrelationId;
    public static Guid? CapturedCorrelationId => _capturedCorrelationId;
    public static void Capture(Guid? correlationId) => _capturedCorrelationId = correlationId;
    public static void Reset() => _capturedCorrelationId = null;
}

internal static class ApSendCorrelationCaptureStore
{
    private static Guid? _capturedCorrelationId;
    public static Guid? CapturedCorrelationId => _capturedCorrelationId;
    public static void Capture(Guid? correlationId) => _capturedCorrelationId = correlationId;
    public static void Reset() => _capturedCorrelationId = null;
}

internal static class ApRequestCorrelationCaptureStore
{
    private static Guid? _capturedCorrelationId;
    public static Guid? CapturedCorrelationId => _capturedCorrelationId;
    public static void Capture(Guid? correlationId) => _capturedCorrelationId = correlationId;
    public static void Reset() => _capturedCorrelationId = null;
}

// ---------------------------------------------------------------------------
// Fakes
// ---------------------------------------------------------------------------

/// Fixed-value ITenantContextAccessor fake — DI-instantiated via WithTenantContext<T>(), so its
/// tenant identity must be a static well-known value rather than constructor-injected.
internal sealed class ApFakeTenantContextAccessor : ITenantContextAccessor
{
    public static readonly Guid FixedTenantId = Guid.NewGuid();

    public Guid? TenantId => FixedTenantId;
}

// ---------------------------------------------------------------------------
// Consumers
// ---------------------------------------------------------------------------

internal sealed class ApPublishCapturingConsumer : IConsumer<ApPublishTestMessage>
{
    public Task Consume(ConsumeContext<ApPublishTestMessage> context)
    {
        ApPublishCorrelationCaptureStore.Capture(context.CorrelationId);
        return Task.CompletedTask;
    }
}

internal sealed class ApSendCapturingConsumer : IConsumer<ApSendTestCommand>
{
    public Task Consume(ConsumeContext<ApSendTestCommand> context)
    {
        ApSendCorrelationCaptureStore.Capture(context.CorrelationId);
        return Task.CompletedTask;
    }
}

internal sealed class ApRequestRespondingConsumer : IConsumer<ApRequestMessage>
{
    public async Task Consume(ConsumeContext<ApRequestMessage> context)
    {
        ApRequestCorrelationCaptureStore.Capture(context.CorrelationId);
        await context.RespondAsync(new ApResponseMessage("pong"));
    }
}
