using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Extensions;
using SharedKernel.Domain;
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
        services.AddSingleton<IPublisher>(MediatRTestPublisher.Instance);

        services.AddSharedKernelApplication();

        var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();
        dispatcher.Should().BeOfType<MediatRDomainEventDispatcher>();
    }

    [Fact]
    public void AddSharedKernelApplication_DoesNotRegisterMediatRItself()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPublisher>(MediatRTestPublisher.Instance);

        services.AddSharedKernelApplication();

        services.Any(d => d.ServiceType == typeof(IMediator)).Should().BeFalse();
        services.Any(d => d.ServiceType == typeof(ISender)).Should().BeFalse();
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

    /// <summary>A minimal no-op <see cref="IPublisher"/> stand-in so <c>AddSharedKernelApplication</c> tests can build a provider.</summary>
    private sealed class MediatRTestPublisher : IPublisher
    {
        public static readonly MediatRTestPublisher Instance = new();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;
    }
}
