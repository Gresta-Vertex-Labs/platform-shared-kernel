using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Pipeline.DomainEvents;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Application.Pipeline.Tests.DomainEvents;

/// <summary>
/// Verifies the serial dispatch semantics of <see cref="DomainEventDispatcher"/>: empty-list no-op,
/// exact runtime-type matching, list and registration order, and exception propagation.
/// </summary>
public sealed class DomainEventDispatcherTests
{
    private sealed record FirstTestEvent : DomainEvent;

    private sealed record SecondTestEvent : DomainEvent;

    private sealed record UnhandledTestEvent : DomainEvent;

    private sealed class Journal
    {
        public List<string> Entries { get; } = [];

        public Exception? ThrowFrom { get; set; }
    }

    private sealed class FirstHandler(Journal journal) : IDomainEventHandler<FirstTestEvent>
    {
        public Task Handle(FirstTestEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Entries.Add("first:a");
            return journal.ThrowFrom is { } exception ? Task.FromException(exception) : Task.CompletedTask;
        }
    }

    private sealed class SecondFirstHandler(Journal journal) : IDomainEventHandler<FirstTestEvent>
    {
        public Task Handle(FirstTestEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Entries.Add("first:b");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondHandler(Journal journal) : IDomainEventHandler<SecondTestEvent>
    {
        public Task Handle(SecondTestEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Entries.Add("second");
            return Task.CompletedTask;
        }
    }

    private sealed class BaseTypeHandler(Journal journal) : IDomainEventHandler<DomainEvent>
    {
        public Task Handle(DomainEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Entries.Add("base");
            return Task.CompletedTask;
        }
    }

    private static (ServiceProvider Provider, Journal Journal) Build()
    {
        var journal = new Journal();
        var services = new ServiceCollection();
        services.AddSingleton(journal);
        // SharedKernel.Application declares no domain-event handler, so only the ones added below are registered.
        services.AddSharedKernelApplication(typeof(SharedKernel.Application.Commands.ICommandScope).Assembly);
        services.AddDomainEventHandler<FirstTestEvent, FirstHandler>();
        services.AddDomainEventHandler<FirstTestEvent, SecondFirstHandler>();
        services.AddDomainEventHandler<SecondTestEvent, SecondHandler>();
        services.AddDomainEventHandler<DomainEvent, BaseTypeHandler>();
        return (services.BuildServiceProvider(), journal);
    }

    private static IDomainEventDispatcher Dispatcher(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

    [Fact]
    public async Task DispatchAsync_WithEmptyList_RunsNothing()
    {
        var (provider, journal) = Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).DispatchAsync([], CancellationToken.None);

        journal.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_RunsEveryHandlerOfEachEvent_InListAndRegistrationOrder()
    {
        var (provider, journal) = Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).DispatchAsync(
            [
                new FirstTestEvent { OccurredOn = DateTimeOffset.UtcNow },
                new SecondTestEvent { OccurredOn = DateTimeOffset.UtcNow },
                new FirstTestEvent { OccurredOn = DateTimeOffset.UtcNow },
            ],
            CancellationToken.None);

        journal.Entries.Should().Equal("first:a", "first:b", "second", "first:a", "first:b");
    }

    [Fact]
    public async Task DispatchAsync_MatchesTheExactRuntimeTypeOnly_AndSkipsAnEventWithNoHandler()
    {
        var (provider, journal) = Build();
        using var scope = provider.CreateScope();

        await Dispatcher(scope).DispatchAsync(
            [new UnhandledTestEvent { OccurredOn = DateTimeOffset.UtcNow }],
            CancellationToken.None);

        journal.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchAsync_WhenAHandlerThrows_PropagatesUnchangedAndStops()
    {
        var (provider, journal) = Build();
        var expected = new InvalidOperationException("handler exploded");
        journal.ThrowFrom = expected;
        using var scope = provider.CreateScope();

        var act = () => Dispatcher(scope).DispatchAsync(
            [new FirstTestEvent { OccurredOn = DateTimeOffset.UtcNow }, new SecondTestEvent { OccurredOn = DateTimeOffset.UtcNow }],
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(expected);
        journal.Entries.Should().Equal("first:a");
    }

    [Fact]
    public async Task DispatchAsync_RepeatedAcrossManyCalls_KeepsWorking()
    {
        var (provider, journal) = Build();
        using var scope = provider.CreateScope();

        for (var i = 0; i < 25; i++)
        {
            await Dispatcher(scope).DispatchAsync(
                [new SecondTestEvent { OccurredOn = DateTimeOffset.UtcNow }],
                CancellationToken.None);
        }

        journal.Entries.Should().HaveCount(25).And.OnlyContain(static entry => entry == "second");
    }
}
