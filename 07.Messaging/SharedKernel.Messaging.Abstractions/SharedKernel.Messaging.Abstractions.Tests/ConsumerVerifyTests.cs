using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.Extensions;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Options;

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
    /// Verifies that <see cref="IMessageBus"/> can be registered as a scoped service
    /// and resolved from a plain <see cref="ServiceCollection"/> without any MassTransit assembly loaded.
    /// </summary>
    [Fact]
    public void IMessageBus_CanBeRegistered_AndResolvedAsInterface_WithoutMassTransit()
    {
        // Arrange — substitute stand-in; no MassTransit concrete type involved
        var services = new ServiceCollection();
        var stub = Substitute.For<IMessageBus>();
        services.AddScoped<IMessageBus>(_ => stub);

        using var provider = services.BuildServiceProvider();

        // Act
        var resolved = provider.GetService<IMessageBus>();

        // Assert
        resolved.Should().NotBeNull();
        resolved.Should().BeSameAs(stub);
    }

    /// <summary>
    /// Verifies that <see cref="IEventPublisher"/> can be registered as a scoped service
    /// and resolved from a plain <see cref="ServiceCollection"/> without any MassTransit assembly loaded.
    /// </summary>
    [Fact]
    public void IEventPublisher_CanBeRegistered_AndResolvedAsInterface_WithoutMassTransit()
    {
        // Arrange
        var services = new ServiceCollection();
        var stub = Substitute.For<IEventPublisher>();
        services.AddScoped<IEventPublisher>(_ => stub);

        using var provider = services.BuildServiceProvider();

        // Act
        var resolved = provider.GetService<IEventPublisher>();

        // Assert
        resolved.Should().NotBeNull();
        resolved.Should().BeSameAs(stub);
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
