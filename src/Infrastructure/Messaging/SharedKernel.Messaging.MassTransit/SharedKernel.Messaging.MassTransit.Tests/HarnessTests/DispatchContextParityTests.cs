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
/// Found by a sample service against a real broker, because both paths still looked
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

    /// <summary>
    /// Both verbs write the actor kind through the one platform mapping (<c>RequestContextPropagation</c>): with no caller
    /// identity it is <c>Anonymous</c>, which readers treat the same as an absent header, and the correlation and tenant
    /// headers keep their names and formats.
    /// </summary>
    [Fact]
    public async Task BothVerbs_WriteTheSameTenantCorrelationAndActorKindHeaders()
    {
        var tenantId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        await using ServiceProvider provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            var tenant = new SharedKernel.Execution.Tenancy.TenantId(tenantId);
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(
                new ParityMessage("payload"),
                ctx => ctx.WithTenantId(tenant).WithHeader(WellKnownHeaders.CorrelationId, correlationId.ToString("D")),
                CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(
                ParityEvent.Create(),
                ctx => ctx.WithTenantId(tenant).WithHeader(WellKnownHeaders.CorrelationId, correlationId.ToString("D")),
                CancellationToken.None);
        }

        (await harness.Published.Any<ParityMessage>()).Should().BeTrue();
        (await harness.Published.Any<EventEnvelope<ParityEvent>>()).Should().BeTrue();

        var message = harness.Published.Select<ParityMessage>().First().Context.Headers;
        var envelope = harness.Published.Select<EventEnvelope<ParityEvent>>().First().Context.Headers;

        foreach (var headers in new[] { message, envelope })
        {
            headers.Get<string>(WellKnownHeaders.TenantId).Should().Be(tenantId.ToString("D"));
            headers.Get<string>(WellKnownHeaders.CorrelationId).Should().Be(correlationId.ToString("D"));
            headers.Get<string>(WellKnownHeaders.ActorKind).Should().Be("Anonymous");
            headers.Get<string>(WellKnownHeaders.ActorId).Should().BeNull("no identity is stamped by the pipe");
            headers.Get<string>(WellKnownHeaders.ClientId).Should().BeNull();
        }

        await harness.Stop();
    }

    /// <summary>
    /// A caller-supplied actor kind overwrites the pipe's <c>Anonymous</c> default, so the explicit-wins rule holds for the
    /// header the platform mapping now always writes.
    /// </summary>
    [Fact]
    public async Task ExplicitActorKind_OverwritesTheAnonymousDefault()
    {
        await using ServiceProvider provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(
                new ParityMessage("explicit-kind"),
                ctx => ctx.WithHeader(WellKnownHeaders.ActorKind, "Service"),
                CancellationToken.None);
        }

        (await harness.Published.Any<ParityMessage>()).Should().BeTrue();

        harness.Published.Select<ParityMessage>().First()
            .Context.Headers.Get<string>(WellKnownHeaders.ActorKind)
            .Should().Be("Service", "caller-supplied headers are applied last");

        await harness.Stop();
    }

    /// <summary>
    /// <c>PublishContextPipe</c> never writes a correlation or tenant header directly: those names are written by
    /// <c>RequestContextPropagation</c> alone.
    /// </summary>
    [Fact]
    public void PublishContextPipe_DoesNotWriteWellKnownHeadersDirectly()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src", "Infrastructure", "Messaging", "SharedKernel.Messaging.MassTransit", "Internal", "PublishContextPipe.cs"));

        source.Should().NotContain("Headers.Set(WellKnownHeaders.TenantId");
        source.Should().NotContain("Headers.Set(WellKnownHeaders.CorrelationId");
        source.Should().Contain("RequestContextPropagation.WriteHeaders");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root containing Platform.SharedKernel.slnx was not found.");
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
