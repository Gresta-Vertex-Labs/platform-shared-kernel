using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Options;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.Tests.DomainEvents;

/// <summary>
/// Verifies <see cref="MediatRDomainEventDispatcher"/> parallel dispatch semantics (T-24):
/// serial ordering preserved by default, parallel all-succeed, and parallel some-fail with
/// all exceptions collected in an <see cref="AggregateException"/>.
/// </summary>
public sealed class MediatRDomainEventDispatcherParallelTests
{
    private sealed record OrderedEvent(int Index) : DomainEvent;

    private sealed record AlwaysSucceedsEvent : DomainEvent;

    private sealed record FailingEvent(string Message) : DomainEvent;

    /// <summary>
    /// Recording publisher that tracks publication order and supports per-event failure injection.
    /// </summary>
    private sealed class RecordingPublisher : IPublisher
    {
        private readonly Func<object, CancellationToken, Task>? _onPublish;
        private readonly object _lock = new();
        public List<object> Published { get; } = [];

        public RecordingPublisher(Func<object, CancellationToken, Task>? onPublish = null)
        {
            _onPublish = onPublish;
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => PublishCore(notification, cancellationToken);

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => PublishCore(notification!, cancellationToken);

        private async Task PublishCore(object notification, CancellationToken cancellationToken)
        {
            if (_onPublish is not null)
                await _onPublish(notification, cancellationToken).ConfigureAwait(false);

            lock (_lock)
                Published.Add(notification);
        }
    }

    [Fact]
    public async Task DispatchAsync_SerialDefault_EventsDispatchedInInsertionOrder()
    {
        // Arrange — serial dispatch (default) must preserve list order.
        var publishOrder = new List<int>();
        var publisher = new RecordingPublisher(async (notification, _) =>
        {
            if (notification is DomainEventNotification<OrderedEvent> n)
            {
                // Simulate a brief, variable async delay to give any concurrent execution
                // a chance to produce out-of-order results.
                await Task.Delay(10 * (3 - n.DomainEvent.Index), CancellationToken.None);
                lock (publishOrder)
                    publishOrder.Add(n.DomainEvent.Index);
            }
        });

        var options = new MediatRDomainEventDispatcherOptions { ParallelDispatch = false };
        var dispatcher = new MediatRDomainEventDispatcher(publisher, Options.Create(options));

        var events = Enumerable.Range(1, 3)
            .Select(i => (IDomainEvent)new OrderedEvent(i) { OccurredOn = DateTimeOffset.UtcNow })
            .ToList();

        // Act
        await dispatcher.DispatchAsync(events.AsReadOnly(), CancellationToken.None);

        // Assert — serial path must honour the original list order.
        publishOrder.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public async Task DispatchAsync_ParallelAllSucceed_AllEventsDispatched()
    {
        // Arrange
        var publisher = new RecordingPublisher();
        var options = new MediatRDomainEventDispatcherOptions { ParallelDispatch = true };
        var dispatcher = new MediatRDomainEventDispatcher(publisher, Options.Create(options));

        var events = Enumerable.Range(0, 5)
            .Select(_ => (IDomainEvent)new AlwaysSucceedsEvent { OccurredOn = DateTimeOffset.UtcNow })
            .ToList();

        // Act
        await dispatcher.DispatchAsync(events.AsReadOnly(), CancellationToken.None);

        // Assert — all five events must have been published.
        publisher.Published.Should().HaveCount(5);
    }

    [Fact]
    public async Task DispatchAsync_ParallelSomeFail_AllEventsDispatchedAndExceptionsWrappedInAggregateException()
    {
        // Arrange — second event will fail, but the others must still be dispatched.
        var publishedMessages = new List<string>();
        var lock_ = new object();

        var publisher = new RecordingPublisher(async (notification, _) =>
        {
            await Task.Yield();

            if (notification is DomainEventNotification<FailingEvent> n)
            {
                if (n.DomainEvent.Message == "fail")
                    throw new InvalidOperationException("handler exploded for: fail");

                lock (lock_)
                    publishedMessages.Add(n.DomainEvent.Message);
            }
        });

        var options = new MediatRDomainEventDispatcherOptions { ParallelDispatch = true };
        var dispatcher = new MediatRDomainEventDispatcher(publisher, Options.Create(options));

        var events = new IDomainEvent[]
        {
            new FailingEvent("succeed-1") { OccurredOn = DateTimeOffset.UtcNow },
            new FailingEvent("fail")      { OccurredOn = DateTimeOffset.UtcNow },
            new FailingEvent("succeed-2") { OccurredOn = DateTimeOffset.UtcNow },
        };

        // Act — must throw an AggregateException containing exactly one inner exception.
        var act = async () => await dispatcher.DispatchAsync(events, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<AggregateException>();
        thrown.Which.InnerExceptions.Should().ContainSingle();
        thrown.Which.InnerExceptions[0].Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("handler exploded");

        // All three events must have been attempted, including the ones after the failing event.
        // (The two succeeding events should have been published.)
        publishedMessages.Should().HaveCount(2);
        publishedMessages.Should().Contain("succeed-1");
        publishedMessages.Should().Contain("succeed-2");
    }
}
