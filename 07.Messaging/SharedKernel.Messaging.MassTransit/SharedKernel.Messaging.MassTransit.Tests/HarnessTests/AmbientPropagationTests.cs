#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using System.Collections.ObjectModel;
using System.Diagnostics;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Messaging.MassTransit.EventPublisher;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// SK.07.AmbientPropagation / P-345: <c>MessagingBusBuilder.WithAmbientCorrelationPropagation()</c>
/// and <c>MessagingBusBuilder.WithTenantContext()</c> exercised through the real
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
    public async Task PublishAsync_WithTenantContextRegistered_PopulatesEnvelopeTenantIdFromAccessor()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelMessaging(o => o.ServiceName = "ap-tenant-service")
            .WithTenantContext();

        services.AddMassTransitTestHarness();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new ApIntegrationTestEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        using (RequestContextScope.Begin(new SystemRequestContext([], "ap-caller", ApAmbientTenant.Fixed)))
        {
            await publisher.PublishAsync(evt, CancellationToken.None);
        }

        (await harness.Published.Any<EventEnvelope<ApIntegrationTestEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<ApIntegrationTestEvent>>().First();

        envelope.Context.Message.TenantId.Should().Be(ApAmbientTenant.Fixed.Value,
            "TenantHeaderPropagator must populate PublishContext.TenantId from the ambient request " +
            "context, flowing through into EventEnvelope<TEvent>.TenantId");

        await harness.Stop();
    }

    [Fact]
    public async Task PublishAsync_WithoutTenantContextRegistered_EnvelopeTenantIdIsNull_NoException()
    {
        var services = new ServiceCollection();

        // WithTenantContext() deliberately NOT called — TenantHeaderPropagator is never registered.
        services.AddSharedKernelMessaging(o => o.ServiceName = "ap-no-tenant-service");

        services.AddMassTransitTestHarness();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new ApIntegrationTestEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Act: must not throw, even though no tenant propagator is registered.
        var act = async () => await publisher.PublishAsync(evt, CancellationToken.None);
        await act.Should().NotThrowAsync();

        (await harness.Published.Any<EventEnvelope<ApIntegrationTestEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<ApIntegrationTestEvent>>().First();

        envelope.Context.Message.TenantId.Should().BeNull(
            "WithTenantContext() was not called, so TenantHeaderPropagator is never registered " +
            "and TenantId stays unset — a provable no-op");

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
            .WithTenantContext();

        services.AddMassTransitTestHarness();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new ApIntegrationTestEvent(Guid.NewGuid(), DateTimeOffset.UtcNow);

        var explicitCorrelationId = Guid.NewGuid();
        var explicitTenantId = Guid.NewGuid();

        // Act: an active Activity would otherwise drive CorrelationId, and the registered accessor
        // would otherwise drive TenantId — the explicit callback must win on both.
        using var activity = new Activity("ap-override-test").Start();

        using (RequestContextScope.Begin(new SystemRequestContext([], "ap-caller", ApAmbientTenant.Fixed)))
        {
            await publisher.PublishAsync(
                evt,
                ctx => ctx.WithCorrelationId(explicitCorrelationId).WithTenantId(new TenantId(explicitTenantId)),
                CancellationToken.None);
        }

        (await harness.Published.Any<EventEnvelope<ApIntegrationTestEvent>>()).Should().BeTrue();
        var envelope = harness.Published.Select<EventEnvelope<ApIntegrationTestEvent>>().First();

        envelope.Context.Message.CorrelationId.Should().Be(explicitCorrelationId.ToString("D"),
            "an explicit WithCorrelationId callback must win over AmbientCorrelationHeaderPropagator");
        envelope.Context.Message.TenantId.Should().Be(explicitTenantId,
            "an explicit WithTenantId callback must win over TenantHeaderPropagator");
        envelope.Context.Message.TenantId.Should().NotBe(ApAmbientTenant.Fixed.Value);

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Message types — internal, no 'file' modifier (breaks MassTransit type matching)
// ---------------------------------------------------------------------------

internal sealed record ApPublishTestMessage(string Text);
internal sealed record ApSendTestCommand(string Text);

[IntegrationEvent("tests.messaging.ambient-propagation.integration-test")]
internal sealed record ApIntegrationTestEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

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


internal static class ApAmbientTenant
{
    public static readonly TenantId Fixed = new(Guid.NewGuid());
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

