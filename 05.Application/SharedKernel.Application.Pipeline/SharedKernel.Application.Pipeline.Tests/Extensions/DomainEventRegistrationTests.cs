using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Commands;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.DomainEvents;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.Pipeline.Tests.Extensions;

/// <summary>
/// Domain-event registration: <c>AddSharedKernelApplication</c> registers the native dispatcher and the domain-event
/// handlers of its assemblies; <c>AddDomainEventHandler</c> adds one from elsewhere.
/// </summary>
public sealed class DomainEventRegistrationTests
{
    // SharedKernel.Application declares no handler, so scanning it registers nothing but the pipeline.
    private static readonly System.Reflection.Assembly AssemblyWithoutHandlers = typeof(ICommandScope).Assembly;

    private sealed record TestDomainEvent : DomainEvent;

    private sealed class TestDomainEventHandler : IDomainEventHandler<TestDomainEvent>
    {
        public Task Handle(TestDomainEvent domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed record ScannedDomainEvent : DomainEvent;

    private sealed class ScannedDomainEventHandler : IDomainEventHandler<ScannedDomainEvent>
    {
        public Task Handle(ScannedDomainEvent domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void AddSharedKernelApplication_RegistersTheNativeDispatcher()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelApplication(AssemblyWithoutHandlers);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>().Should().BeOfType<DomainEventDispatcher>();
        services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher)).Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddSharedKernelApplication_RegistersTheDomainEventHandlersOfItsAssemblies_Scoped()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelApplication(typeof(DomainEventRegistrationTests).Assembly);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IDomainEventHandler<ScannedDomainEvent>>()
            .Should().ContainSingle().Which.Should().BeOfType<ScannedDomainEventHandler>();
        services.Single(d => d.ServiceType == typeof(IDomainEventHandler<ScannedDomainEvent>)).Lifetime
            .Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddDomainEventHandler_ForAHandlerTheScanAlsoFinds_RegistersItOnce()
    {
        var services = new ServiceCollection();

        services.AddDomainEventHandler<ScannedDomainEvent, ScannedDomainEventHandler>();
        services.AddSharedKernelApplication(typeof(DomainEventRegistrationTests).Assembly);
        services.AddDomainEventHandler<ScannedDomainEvent, ScannedDomainEventHandler>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IDomainEventHandler<ScannedDomainEvent>>().Should().ContainSingle();
    }

    [Fact]
    public void AddDomainEventHandler_RegistersTheHandlerOnce()
    {
        var services = new ServiceCollection();

        services.AddDomainEventHandler<TestDomainEvent, TestDomainEventHandler>();
        services.AddDomainEventHandler<TestDomainEvent, TestDomainEventHandler>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetServices<IDomainEventHandler<TestDomainEvent>>()
            .Should().ContainSingle().Which.Should().BeOfType<TestDomainEventHandler>();
    }

    [Fact]
    public void AddSharedKernelApplication_WithoutAMediator_RegistersNoSender()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelApplication(AssemblyWithoutHandlers);

        services.Any(d => d.ServiceType == typeof(ISender)).Should().BeFalse();
    }
}
