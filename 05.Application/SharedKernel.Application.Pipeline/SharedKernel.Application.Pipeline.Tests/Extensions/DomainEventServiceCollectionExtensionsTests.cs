using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.DomainEvents;
using SharedKernel.Application.Pipeline.Extensions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.Pipeline.Tests.Extensions;

/// <summary>DI registration tests for <see cref="DomainEventServiceCollectionExtensions"/>.</summary>
public sealed class DomainEventServiceCollectionExtensionsTests
{
    private sealed record TestDomainEvent : DomainEvent;

    private sealed class TestDomainEventHandler : IDomainEventHandler<TestDomainEvent>
    {
        public Task Handle(TestDomainEvent domainEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public void AddSharedKernelDomainEvents_RegistersTheNativeDispatcher()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelDomainEvents();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>().Should().BeOfType<DomainEventDispatcher>();
    }

    [Fact]
    public void AddSharedKernelDomainEvents_RegistersNoMediator_AndIsIdempotent()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelDomainEvents();
        services.AddSharedKernelDomainEvents();

        services.Count(d => d.ServiceType == typeof(IDomainEventDispatcher)).Should().Be(1);
        services.Any(d => d.ServiceType == typeof(ISender)).Should().BeFalse();
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
}
