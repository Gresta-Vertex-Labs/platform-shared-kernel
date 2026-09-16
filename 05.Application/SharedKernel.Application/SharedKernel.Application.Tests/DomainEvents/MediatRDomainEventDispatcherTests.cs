using FluentAssertions;
using MediatR;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.Tests.DomainEvents;

/// <summary>
/// Verifies <see cref="MediatRDomainEventDispatcher"/>'s serial dispatch semantics: empty-list
/// no-op, single-event dispatch, multi-type-event dispatch, and exception propagation.
/// </summary>
public sealed class MediatRDomainEventDispatcherTests
{
    private sealed record FirstTestEvent : DomainEvent;

    private sealed record SecondTestEvent : DomainEvent;

    /// <summary>
    /// A recording <see cref="IPublisher"/> test double. NSubstitute's expression-tree-based
    /// <c>Arg.Is</c>/<c>Arg.Do</c> configuration cannot target MediatR's generic
    /// <c>Publish&lt;TNotification&gt;</c> overload cleanly across multiple closed generic
    /// instantiations, so a small hand-written recorder is used instead.
    /// </summary>
    private sealed class RecordingPublisher(Func<object, CancellationToken, Task>? onPublish = null) : IPublisher
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => PublishCore(notification, cancellationToken);

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => PublishCore(notification!, cancellationToken);

        private Task PublishCore(object notification, CancellationToken cancellationToken)
        {
            Published.Add(notification);
            return onPublish?.Invoke(notification, cancellationToken) ?? Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DispatchAsync_WithEmptyList_DoesNotCallPublish()
    {
        var publisher = new RecordingPublisher();
        var dispatcher = new MediatRDomainEventDispatcher(publisher);

        await dispatcher.DispatchAsync([], CancellationToken.None);

        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_WithSingleEvent_PublishesWrappedNotification()
    {
        var publisher = new RecordingPublisher();
        var dispatcher = new MediatRDomainEventDispatcher(publisher);
        var domainEvent = new FirstTestEvent { OccurredOn = DateTimeOffset.UtcNow };

        await dispatcher.DispatchAsync([domainEvent], CancellationToken.None);

        publisher.Published.Should().ContainSingle();
        var notification = publisher.Published[0].Should().BeOfType<DomainEventNotification<FirstTestEvent>>().Subject;
        notification.DomainEvent.Should().BeSameAs(domainEvent);
    }

    [Fact]
    public async Task DispatchAsync_WithMultipleDifferentEventTypes_PublishesEachAsItsOwnClosedNotification()
    {
        var publisher = new RecordingPublisher();
        var dispatcher = new MediatRDomainEventDispatcher(publisher);
        var first = new FirstTestEvent { OccurredOn = DateTimeOffset.UtcNow };
        var second = new SecondTestEvent { OccurredOn = DateTimeOffset.UtcNow };

        await dispatcher.DispatchAsync([first, second], CancellationToken.None);

        publisher.Published.Should().HaveCount(2);
        var firstNotification = publisher.Published.OfType<DomainEventNotification<FirstTestEvent>>().Should().ContainSingle().Subject;
        firstNotification.DomainEvent.Should().BeSameAs(first);
        var secondNotification = publisher.Published.OfType<DomainEventNotification<SecondTestEvent>>().Should().ContainSingle().Subject;
        secondNotification.DomainEvent.Should().BeSameAs(second);
    }

    [Fact]
    public async Task DispatchAsync_MultipleEvents_PublishedInListOrder()
    {
        var publishOrder = new List<int>();
        var publisher = new RecordingPublisher((notification, _) =>
        {
            if (notification is DomainEventNotification<FirstTestEvent>)
                publishOrder.Add(1);
            else if (notification is DomainEventNotification<SecondTestEvent>)
                publishOrder.Add(2);
            return Task.CompletedTask;
        });
        var dispatcher = new MediatRDomainEventDispatcher(publisher);

        await dispatcher.DispatchAsync(
            [
                new FirstTestEvent { OccurredOn = DateTimeOffset.UtcNow },
                new SecondTestEvent { OccurredOn = DateTimeOffset.UtcNow },
                new FirstTestEvent { OccurredOn = DateTimeOffset.UtcNow },
            ],
            CancellationToken.None);

        publishOrder.Should().Equal(1, 2, 1);
    }

    [Fact]
    public async Task DispatchAsync_WhenPublishThrows_PropagatesExceptionUnchanged()
    {
        var expected = new InvalidOperationException("handler exploded");
        var publisher = new RecordingPublisher((_, _) => Task.FromException(expected));
        var dispatcher = new MediatRDomainEventDispatcher(publisher);
        var domainEvent = new FirstTestEvent { OccurredOn = DateTimeOffset.UtcNow };

        var act = async () => await dispatcher.DispatchAsync([domainEvent], CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expected);
    }

    /// <summary>
    /// The compiled notification-factory delegate is cached once per concrete event <see cref="Type"/>
    /// in a static <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>, shared
    /// across every <see cref="MediatRDomainEventDispatcher"/> instance and every dispatch. This proves
    /// dispatch still works correctly across many calls and several event types — including repeats —
    /// and that the very same compiled delegate instance is reused rather than rebuilt, by reading the
    /// static cache field via reflection (this test project has no access to the internal factory type
    /// any other way, by design).
    /// </summary>
    [Fact]
    public async Task DispatchAsync_RepeatedAcrossManyCallsAndEventTypes_ReusesCompiledFactoryPerType()
    {
        var cacheField = typeof(MediatRDomainEventDispatcher)
            .GetField("NotificationFactories", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var cache = (System.Collections.Concurrent.ConcurrentDictionary<Type, Func<IDomainEvent, INotification>>)cacheField.GetValue(null)!;

        var publisher = new RecordingPublisher();
        var dispatcher = new MediatRDomainEventDispatcher(publisher);

        // First dispatch of each type populates the cache.
        await dispatcher.DispatchAsync(
            [new FirstTestEvent { OccurredOn = DateTimeOffset.UtcNow }, new SecondTestEvent { OccurredOn = DateTimeOffset.UtcNow }],
            CancellationToken.None);

        cache.Should().ContainKey(typeof(FirstTestEvent));
        cache.Should().ContainKey(typeof(SecondTestEvent));
        var firstFactory = cache[typeof(FirstTestEvent)];
        var secondFactory = cache[typeof(SecondTestEvent)];

        // Dispatching many more events of the same two types must never rebuild the delegate.
        for (var i = 0; i < 25; i++)
        {
            await dispatcher.DispatchAsync(
                [new FirstTestEvent { OccurredOn = DateTimeOffset.UtcNow }, new SecondTestEvent { OccurredOn = DateTimeOffset.UtcNow }],
                CancellationToken.None);
        }

        cache[typeof(FirstTestEvent)].Should().BeSameAs(firstFactory);
        cache[typeof(SecondTestEvent)].Should().BeSameAs(secondFactory);
        publisher.Published.Should().HaveCount(2 + (25 * 2));
        publisher.Published.OfType<DomainEventNotification<FirstTestEvent>>().Should().HaveCount(26);
        publisher.Published.OfType<DomainEventNotification<SecondTestEvent>>().Should().HaveCount(26);
    }
}
