#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using System.Collections.ObjectModel;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.EventPublisher;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// P-561: every dispatch verb writes the same publish context onto the transport.
/// </summary>
/// <remarks>
/// <para>
/// <c>IMessageBus</c> and <c>IEventPublisher</c> each used to map the context themselves, and they
/// drifted. The event path never wrote the tenant header at all — a tenant reached the CloudEvents
/// envelope's body and nothing else, so a consume filter, which cannot deserialize a payload it has
/// no type for, saw no tenant. It also stamped the transport correlation id only when the caller
/// happened to supply a custom header or a partition key, so the ordinary publish lost it.
/// </para>
/// <para>
/// Found by <c>samples/ShippingApi</c> against a real broker, because both paths still looked
/// correct in isolation: the envelope carried the right tenant, and the consumer simply had none.
/// These tests are the guard that keeps the two verbs in step.
/// </para>
/// </remarks>
public sealed class DispatchContextParityTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddMassTransitTestHarness(cfg =>
            cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx)));

        services.AddSingleton<IReadOnlyDictionary<Type, string>>(
            new ReadOnlyDictionary<Type, string>(new Dictionary<Type, string>()));
        services.AddScoped<ConventionSendEndpointResolver>();
        services.AddScoped<IMessageBus, MassTransitMessageBus>();
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
        services.Configure<MessagingOptions>(o => o.ServiceName = "parity-service");

        return services.BuildServiceProvider(true);
    }

    /// <summary>
    /// <c>IEventPublisher</c> writes the tenant as a transport header, not only into the envelope.
    /// </summary>
    [Fact]
    public async Task EventPublisher_WithTenant_WritesTheTenantTransportHeader()
    {
        var tenantId = Guid.NewGuid();

        await using ServiceProvider provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            (await publisher.PublishAsync(
                ParityEvent.Create(),
                ctx => ctx.WithTenantId(new SharedKernel.Execution.Tenancy.TenantId(tenantId)),
                CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        (await harness.Published.Any<EventEnvelope<ParityEvent>>()).Should().BeTrue();
        IPublishedMessage published = harness.Published.Select<EventEnvelope<ParityEvent>>().First();

        published.Context.Headers.Get<string>(WellKnownHeaders.TenantId)
            .Should().Be(tenantId.ToString("D"),
                "a consume filter reads the header; it cannot read the serialized envelope body");

        harness.Published.Select<EventEnvelope<ParityEvent>>().First()
            .MessageObject.Should().BeOfType<EventEnvelope<ParityEvent>>()
            .Which.TenantId.Should().Be(tenantId, "the envelope keeps carrying it too, for the consumer");

        await harness.Stop();
    }

    /// <summary>
    /// <c>IMessageBus</c> writes the same header under the same name, so one consume filter serves
    /// both verbs.
    /// </summary>
    [Fact]
    public async Task MessageBus_WithTenant_WritesTheSameTransportHeader()
    {
        var tenantId = Guid.NewGuid();

        await using ServiceProvider provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            (await bus.PublishAsync(
                new ParityMessage("payload"),
                ctx => ctx.WithTenantId(new SharedKernel.Execution.Tenancy.TenantId(tenantId)),
                CancellationToken.None)).IsSuccess.Should().BeTrue();
        }

        (await harness.Published.Any<ParityMessage>()).Should().BeTrue();

        harness.Published.Select<ParityMessage>().First()
            .Context.Headers.Get<string>(WellKnownHeaders.TenantId)
            .Should().Be(tenantId.ToString("D"));

        await harness.Stop();
    }

    /// <summary>
    /// A plain event publish — no custom header, no partition key — still carries the transport
    /// correlation id. That combination used to take a fast path that skipped the pipe entirely.
    /// </summary>
    [Fact]
    public async Task EventPublisher_WithOnlyACorrelationId_StillStampsItOnTheTransport()
    {
        var correlationId = Guid.NewGuid();

        await using ServiceProvider provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            await publisher.PublishAsync(
                ParityEvent.Create(),
                ctx => ctx.WithCorrelationId(correlationId),
                CancellationToken.None);
        }

        (await harness.Published.Any<EventEnvelope<ParityEvent>>()).Should().BeTrue();

        harness.Published.Select<EventEnvelope<ParityEvent>>().First()
            .Context.CorrelationId.Should().Be(correlationId);

        await harness.Stop();
    }

    /// <summary>
    /// A caller-supplied header still wins over the derived tenant header, so an explicit value on
    /// one publish is never silently overwritten.
    /// </summary>
    [Fact]
    public async Task ExplicitHeader_OverridesTheDerivedTenantHeader()
    {
        var ambient = Guid.NewGuid();

        await using ServiceProvider provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(
                new ParityMessage("payload"),
                ctx => ctx.WithTenantId(new SharedKernel.Execution.Tenancy.TenantId(ambient)).WithHeader(WellKnownHeaders.TenantId, "explicit"),
                CancellationToken.None);
        }

        (await harness.Published.Any<ParityMessage>()).Should().BeTrue();

        harness.Published.Select<ParityMessage>().First()
            .Context.Headers.Get<string>(WellKnownHeaders.TenantId)
            .Should().Be("explicit", "caller-supplied headers are applied last");

        await harness.Stop();
    }
}

/// <summary>The plain message used by these tests.</summary>
/// <param name="Text">Ignored.</param>
public sealed record ParityMessage(string Text);

/// <summary>The integration event used by these tests.</summary>
/// <param name="EventId">The occurrence id.</param>
/// <param name="OccurredOn">When the fact happened.</param>
[IntegrationEvent("tests.messaging.parity.event")]
public sealed record ParityEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent
{
    /// <summary>Creates a valid instance.</summary>
    /// <returns>A new event.</returns>
    public static ParityEvent Create() => new(Guid.NewGuid(), DateTimeOffset.UtcNow);
}
