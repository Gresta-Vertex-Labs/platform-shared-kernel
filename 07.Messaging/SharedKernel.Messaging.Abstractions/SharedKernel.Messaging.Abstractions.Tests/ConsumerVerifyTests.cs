using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.Extensions;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Testing.Messaging;

namespace SharedKernel.Messaging.Abstractions.Tests;

/// <summary>
/// Consumer-verify tests for <c>SharedKernel.Messaging.Abstractions</c> in isolation.
/// Confirms the abstractions package carries no transport NuGet dependency and that
/// <see cref="IMessageBus"/> and <see cref="IEventPublisher"/> are usable as pure
/// interface contracts without any MassTransit reference present.
/// </summary>
public sealed class ConsumerVerifyTests
{
    /// <summary>
    /// Verifies that <see cref="IMessageBus"/> can be registered as a service and resolved from a
    /// plain <see cref="ServiceCollection"/> without any MassTransit assembly loaded, using the
    /// <see cref="InMemoryMessageBus"/> test double from <c>SharedKernel.Testing</c> rather than a
    /// mocking-framework substitute.
    /// </summary>
    [Fact]
    public void IMessageBus_CanBeRegistered_AndResolvedAsInterface_WithoutMassTransit()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInMemoryMessageBus();

        using var provider = services.BuildServiceProvider();

        // Act
        var resolved = provider.GetService<IMessageBus>();
        var recorder = provider.GetRequiredService<InMemoryMessageBus>();

        // Assert
        resolved.Should().NotBeNull();
        resolved.Should().BeSameAs(recorder);
    }

    /// <summary>
    /// Verifies that <see cref="IMessageBus"/>, resolved purely as an interface with zero
    /// MassTransit assembly loaded, correctly records a published and a sent message — proving the
    /// resolved instance is a real, working implementation and not just a non-null stand-in.
    /// </summary>
    [Fact]
    public async Task IMessageBus_ResolvedAsInterface_RecordsPublishAndSend_WithoutMassTransit()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInMemoryMessageBus();

        using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IMessageBus>();
        var recorder = provider.GetRequiredService<InMemoryMessageBus>();

        var published = new TestIntegrationMessage("published-payload");
        var sent = new TestIntegrationMessage("sent-payload");

        // Act
        await bus.PublishAsync(published, CancellationToken.None);
        await bus.SendAsync(sent, CancellationToken.None);

        // Assert
        recorder.ShouldHavePublished<TestIntegrationMessage>().Should().BeSameAs(published);
        recorder.ShouldHaveSent<TestIntegrationMessage>().Should().BeSameAs(sent);
    }

    /// <summary>
    /// Verifies that <see cref="IEventPublisher"/> can be registered as a service and resolved from
    /// a plain <see cref="ServiceCollection"/> without any MassTransit assembly loaded, using the
    /// <see cref="InMemoryEventPublisher"/> test double from <c>SharedKernel.Testing</c> rather than
    /// a mocking-framework substitute.
    /// </summary>
    [Fact]
    public void IEventPublisher_CanBeRegistered_AndResolvedAsInterface_WithoutMassTransit()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInMemoryEventPublisher();

        using var provider = services.BuildServiceProvider();

        // Act
        var resolved = provider.GetService<IEventPublisher>();
        var recorder = provider.GetRequiredService<InMemoryEventPublisher>();

        // Assert
        resolved.Should().NotBeNull();
        resolved.Should().BeSameAs(recorder);
    }

    /// <summary>
    /// Verifies that <see cref="IEventPublisher"/>, resolved purely as an interface with zero
    /// MassTransit assembly loaded, correctly records a published integration event — proving the
    /// resolved instance is a real, working implementation and not just a non-null stand-in.
    /// </summary>
    [Fact]
    public async Task IEventPublisher_ResolvedAsInterface_RecordsPublishedEvent_WithoutMassTransit()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddInMemoryEventPublisher();

        using var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IEventPublisher>();
        var recorder = provider.GetRequiredService<InMemoryEventPublisher>();

        var integrationEvent = new TestIntegrationEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, "integration-event-payload");

        // Act
        await publisher.PublishAsync(integrationEvent, CancellationToken.None);

        // Assert
        recorder.ShouldHavePublished<TestIntegrationEvent>().Should().BeSameAs(integrationEvent);
    }

    /// <summary>
    /// Verifies that the Abstractions assembly does not load MassTransit-specific types.
    /// Concretely: no type in the assembly should reference MassTransit namespaces directly.
    /// </summary>
    [Fact]
    public void AbstractionsAssembly_ReferencesNo_MassTransit_Assembly()
    {
        var abstractionsAssembly = typeof(IMessageBus).Assembly;

        var massTransitRefs = abstractionsAssembly
            .GetReferencedAssemblies()
            .Where(a => a.Name != null && a.Name.StartsWith("MassTransit", StringComparison.OrdinalIgnoreCase))
            .ToList();

        massTransitRefs.Should().BeEmpty(
            "SharedKernel.Messaging.Abstractions must have zero MassTransit assembly references — it is a transport-agnostic interface library");
    }

    /// <summary>
    /// Verifies that <see cref="MessagingOptions"/> is a plain POCO sealed class
    /// with a <c>ServiceName</c> property — usable without any transport dependency.
    /// </summary>
    [Fact]
    public void MessagingOptions_IsSealed_HasServiceName_PropertyAssignable_WithoutMassTransit()
    {
        // Arrange & Act — direct instantiation; no DI or MassTransit required
        var opts = new MessagingOptions { ServiceName = "consumer-verify-service" };

        // Assert
        typeof(MessagingOptions).IsSealed.Should().BeTrue();
        opts.ServiceName.Should().Be("consumer-verify-service");
    }

    /// <summary>
    /// Verifies that <see cref="IMessagingBuilder"/> is an interface (not a concrete class)
    /// and that its <c>Services</c> property can be satisfied using only
    /// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> — zero MassTransit dependency.
    /// </summary>
    [Fact]
    public void IMessagingBuilder_IsInterface_AndServicesProperty_UsesOnlyDIAbstractions()
    {
        // Compile-time proof: IMessagingBuilder.Services is IServiceCollection from DI.Abstractions
        typeof(IMessagingBuilder).IsInterface.Should().BeTrue();

        var prop = typeof(IMessagingBuilder).GetProperty(nameof(IMessagingBuilder.Services));
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(IServiceCollection));
    }

    /// <summary>
    /// Verifies that <see cref="PublishContext"/> carries the required fluent methods
    /// and can be constructed and used without any MassTransit reference.
    /// </summary>
    [Fact]
    public void PublishContext_FluentChain_WorksInIsolation_WithoutMassTransit()
    {
        // Arrange & Act
        var correlationId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        var ctx = new PublishContext()
            .WithCorrelationId(correlationId)
            .WithCausationId(causationId)
            .WithHeader("x-tenant", "acme");

        // Assert
        ctx.CorrelationId.Should().Be(correlationId);
        ctx.CausationId.Should().Be(causationId);
        ctx.Headers.Should().ContainKey("x-tenant")
            .WhoseValue.Should().Be("acme");
    }
}

/// <summary>
/// Minimal message type used to exercise <see cref="IMessageBus"/> publish/send recording in
/// <see cref="ConsumerVerifyTests"/> — carries no transport dependency.
/// </summary>
/// <param name="Payload">An arbitrary string payload distinguishing one test message from another.</param>
public sealed record TestIntegrationMessage(string Payload);

/// <summary>
/// Minimal integration event used to exercise <see cref="IEventPublisher"/> publish recording in
/// <see cref="ConsumerVerifyTests"/> — carries no transport dependency.
/// </summary>
/// <param name="EventId">The unique identifier of this event occurrence.</param>
/// <param name="OccurredOn">The time the event occurred.</param>
/// <param name="Payload">An arbitrary string payload distinguishing one test event from another.</param>
[IntegrationEvent("tests.messaging.abstractions.consumer-verify-event")]
public sealed record TestIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn, string Payload) : IIntegrationEvent;
