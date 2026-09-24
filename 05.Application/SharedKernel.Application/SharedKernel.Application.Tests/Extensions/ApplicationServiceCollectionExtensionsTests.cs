using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.Tests.Extensions;

/// <summary>
/// DI resolution tests for <see cref="ApplicationServiceCollectionExtensions"/>.
/// </summary>
public sealed class ApplicationServiceCollectionExtensionsTests
{
    private sealed record TestDomainEvent : DomainEvent;

    private sealed class TestDomainEventHandler : IDomainEventHandler<TestDomainEvent>
    {
        public Task Handle(TestDomainEvent domainEvent, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    [Fact]
    public void AddSharedKernelApplication_RegistersDomainEventDispatcherAsMediatRDomainEventDispatcher()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelApplication(typeof(ApplicationServiceCollectionExtensionsTests).Assembly);

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();
        dispatcher.Should().BeOfType<MediatRDomainEventDispatcher>();
    }

    [Fact]
    public void AddSharedKernelApplication_RegistersMediatR()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelApplication(typeof(ApplicationServiceCollectionExtensionsTests).Assembly);

        services.Any(d => d.ServiceType == typeof(IMediator)).Should().BeTrue();
        services.Any(d => d.ServiceType == typeof(ISender)).Should().BeTrue();
    }

    [Fact]
    public void AddDomainEventHandler_RegistersHandlerAndNotificationAdapter()
    {
        var services = new ServiceCollection();

        services.AddDomainEventHandler<TestDomainEvent, TestDomainEventHandler>();

        var provider = services.BuildServiceProvider();

        var handler = provider.GetRequiredService<IDomainEventHandler<TestDomainEvent>>();
        handler.Should().BeOfType<TestDomainEventHandler>();

        var notificationHandler = provider
            .GetRequiredService<INotificationHandler<DomainEventNotification<TestDomainEvent>>>();
        notificationHandler.Should().NotBeNull();
    }

    [Fact]
    public async Task AddDomainEventHandler_NotificationAdapter_ForwardsToRegisteredHandler()
    {
        // Verifies the internal DomainEventNotificationHandler<TDomainEvent> adapter (registered
        // exclusively via AddDomainEventHandler<,>) correctly unwraps and forwards to the handler —
        // exercised purely through the public AddDomainEventHandler<,> + DI resolution surface,
        // since the adapter type itself is internal and not part of the public contract.
        var services = new ServiceCollection();
        var tcs = new TaskCompletionSource<TestDomainEvent>();
        services.AddSingleton(tcs);
        services.AddDomainEventHandler<TestDomainEvent, RecordingHandler>();

        var provider = services.BuildServiceProvider();
        var notificationHandler = provider
            .GetRequiredService<INotificationHandler<DomainEventNotification<TestDomainEvent>>>();
        var domainEvent = new TestDomainEvent { OccurredOn = DateTimeOffset.UtcNow };

        await notificationHandler.Handle(new DomainEventNotification<TestDomainEvent>(domainEvent), CancellationToken.None);

        var received = await tcs.Task;
        received.Should().Be(domainEvent);
    }

    private sealed class RecordingHandler(TaskCompletionSource<TestDomainEvent> tcs) : IDomainEventHandler<TestDomainEvent>
    {
        public Task Handle(TestDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            tcs.SetResult(domainEvent);
            return Task.CompletedTask;
        }
    }
}
