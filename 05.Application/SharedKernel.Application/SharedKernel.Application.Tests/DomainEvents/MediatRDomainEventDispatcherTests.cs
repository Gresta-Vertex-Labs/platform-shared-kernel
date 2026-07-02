using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Options;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.Tests.DomainEvents;

/// <summary>
/// Verifies <see cref="MediatRDomainEventDispatcher"/>'s dispatch semantics: empty-list no-op,
/// single-event dispatch, multi-type-event dispatch, and exception propagation.
/// </summary>
public sealed class MediatRDomainEventDispatcherTests
{
    private sealed record FirstTestEvent : DomainEvent;

    private sealed record SecondTestEvent : DomainEvent;

    /// <summary>
    /// A recording <see cref="IPublisher"/> test double. NSubstitute's expression-tree-based
    /// <c>Arg.Is</c>/<c>Arg.Do</c> configuration cannot target MediatR's generic
    /// <c>Publish&lt;TNotification&gt;</c> overload (the one C# overload resolution actually picks
    /// for a statically-typed <see cref="INotification"/> argument such as
    /// <see cref="DomainEventNotification{TDomainEvent}"/>) cleanly across multiple closed generic
    /// instantiations, so a small hand-written recorder is used instead — it satisfies the same
    /// "minimal real collaborator over a hand-rolled delegate mock" spirit without fighting
    /// NSubstitute's generic-method matching.
    /// </summary>
    private sealed class RecordingPublisher : IPublisher
    {
        private readonly Func<object, CancellationToken, Task>? _onPublish;

        public RecordingPublisher(Func<object, CancellationToken, Task>? onPublish = null)
        {
            _onPublish = onPublish;
        }

        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => PublishCore(notification, cancellationToken);

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => PublishCore(notification!, cancellationToken);

        private Task PublishCore(object notification, CancellationToken cancellationToken)
        {
            Published.Add(notification);
            return _onPublish?.Invoke(notification, cancellationToken) ?? Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DispatchAsync_WithEmptyList_DoesNotCallPublish()
    {
        var publisher = new RecordingPublisher();
        var dispatcher = new MediatRDomainEventDispatcher(publisher, Options.Create(new MediatRDomainEventDispatcherOptions()));

        await dispatcher.DispatchAsync([], CancellationToken.None);

        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_WithSingleEvent_PublishesWrappedNotification()
    {
        var publisher = new RecordingPublisher();
        var dispatcher = new MediatRDomainEventDispatcher(publisher, Options.Create(new MediatRDomainEventDispatcherOptions()));
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
        var dispatcher = new MediatRDomainEventDispatcher(publisher, Options.Create(new MediatRDomainEventDispatcherOptions()));
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
    public async Task DispatchAsync_WhenPublishThrows_PropagatesExceptionUnchanged()
    {
        var expected = new InvalidOperationException("handler exploded");
        var publisher = new RecordingPublisher((_, _) => Task.FromException(expected));
        var dispatcher = new MediatRDomainEventDispatcher(publisher, Options.Create(new MediatRDomainEventDispatcherOptions()));
        var domainEvent = new FirstTestEvent { OccurredOn = DateTimeOffset.UtcNow };

        var act = async () => await dispatcher.DispatchAsync([domainEvent], CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Should().BeSameAs(expected);
    }
}
