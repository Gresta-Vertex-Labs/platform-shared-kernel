using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Extensions;
using SharedKernel.Application.Messaging;
using SharedKernel.Domain;
using SharedKernel.Domain.Events;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Tests;

/// <summary>
/// Consumer-verify tests for <c>SharedKernel.Application</c> in isolation. Proves the full DI
/// registration chain a real consuming service would wire up — <c>AddMediatR</c> +
/// <c>AddSharedKernelApplication()</c> + <c>AddDomainEventHandler&lt;,&gt;</c> — resolves and
/// executes with zero exceptions, end to end, mirroring the pattern established by
/// <c>07.Messaging</c>'s <c>ConsumerVerifyTests</c>.
/// </summary>
public sealed class ConsumerVerifyTests
{
    private sealed record CreateWidgetCommand(string Name) : ICommand<Guid>;

    private sealed class CreateWidgetCommandHandler : ICommandHandler<CreateWidgetCommand, Guid>
    {
        public Task<Result<Guid>> Handle(CreateWidgetCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result<Guid>.Success(Guid.NewGuid()));
    }

    private sealed record GetWidgetNameQuery(Guid Id) : IQuery<string>;

    private sealed class GetWidgetNameQueryHandler : IQueryHandler<GetWidgetNameQuery, string>
    {
        public Task<Result<string>> Handle(GetWidgetNameQuery request, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Success("widget"));
    }

    private sealed record WidgetCreatedDomainEvent : DomainEvent;

    private sealed class WidgetCreatedDomainEventHandler(TaskCompletionSource<WidgetCreatedDomainEvent> tcs)
        : IDomainEventHandler<WidgetCreatedDomainEvent>
    {
        public Task Handle(WidgetCreatedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            tcs.SetResult(domainEvent);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Builds a service collection exactly as documented in <c>CLAUDE.md</c>'s "DI Registration"
    /// section — the consuming service owns <c>AddMediatR</c>; this package only adds the
    /// dispatcher bridge and the domain-event-handler registration.
    /// </summary>
    private static ServiceProvider BuildConsumerProvider(TaskCompletionSource<WidgetCreatedDomainEvent> tcs)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tcs);

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ConsumerVerifyTests>());
        services.AddSharedKernelApplication();
        services.AddDomainEventHandler<WidgetCreatedDomainEvent, WidgetCreatedDomainEventHandler>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void FullDIChain_Resolves_WithZeroExceptions()
    {
        var tcs = new TaskCompletionSource<WidgetCreatedDomainEvent>();

        var act = () => BuildConsumerProvider(tcs);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task FullDIChain_SendCommand_ExecutesHandlerAndReturnsSuccess()
    {
        var tcs = new TaskCompletionSource<WidgetCreatedDomainEvent>();
        using var provider = BuildConsumerProvider(tcs);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new CreateWidgetCommand("widget-1"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task FullDIChain_SendQuery_ExecutesHandlerAndReturnsSuccess()
    {
        var tcs = new TaskCompletionSource<WidgetCreatedDomainEvent>();
        using var provider = BuildConsumerProvider(tcs);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new GetWidgetNameQuery(Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("widget");
    }

    [Fact]
    public async Task FullDIChain_DispatchDomainEvent_ThroughMediatRDomainEventDispatcher_ReachesRegisteredHandler()
    {
        var tcs = new TaskCompletionSource<WidgetCreatedDomainEvent>();
        using var provider = BuildConsumerProvider(tcs);
        var dispatcher = provider.GetRequiredService<IDomainEventDispatcher>();
        var domainEvent = new WidgetCreatedDomainEvent { OccurredOn = DateTimeOffset.UtcNow };

        await dispatcher.DispatchAsync([domainEvent], CancellationToken.None);

        var received = await tcs.Task;
        received.Should().Be(domainEvent);
    }
}
