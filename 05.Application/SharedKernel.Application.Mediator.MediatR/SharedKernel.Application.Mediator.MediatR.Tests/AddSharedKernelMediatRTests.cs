using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Extensions;
using SharedKernel.Application.Streaming;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Mediator.MediatR.Tests;

/// <summary>
/// Proves the whole registration chain a consuming service wires — <c>AddSharedKernelMediatR</c> plus
/// <c>AddSharedKernelApplicationBehaviors()...Build()</c> — sends commands, queries and streams through
/// the kernel pipeline and dispatches domain events, all through kernel contracts only.
/// </summary>
public sealed class AddSharedKernelMediatRTests
{
    public sealed record CreateWidgetCommand(string Name) : ICommand<Guid>;

    public sealed class CreateWidgetCommandHandler : ICommandHandler<CreateWidgetCommand, Guid>
    {
        public Task<Result<Guid>> Handle(CreateWidgetCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result<Guid>.Success(Guid.NewGuid()));
    }

    public sealed record GetWidgetNameQuery(Guid Id) : IQuery<string>;

    private sealed class GetWidgetNameQueryHandler : IQueryHandler<GetWidgetNameQuery, string>
    {
        public Task<Result<string>> Handle(GetWidgetNameQuery request, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Success("widget"));
    }

    public sealed record ExportRowsStreamQuery(int Count) : IStreamQuery<int>;

    private sealed class ExportRowsStreamQueryHandler : IStreamQueryHandler<ExportRowsStreamQuery, int>
    {
        public async IAsyncEnumerable<int> Handle(
            ExportRowsStreamQuery request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < request.Count; i++)
            {
                await Task.Yield();
                yield return i;
            }
        }
    }

    /// <summary>Sends a nested command from its handler, to prove both run in one DI scope.</summary>
    public sealed record OuterCommand : ICommand<bool>;

    public sealed record InnerCommand : ICommand<ScopeProbe>;

    public sealed class ScopeProbe;

    private sealed class OuterCommandHandler(ISender sender, ScopeProbe probe) : ICommandHandler<OuterCommand, bool>
    {
        public async Task<Result<bool>> Handle(OuterCommand request, CancellationToken cancellationToken)
        {
            var inner = await sender.Send(new InnerCommand(), cancellationToken);
            return Result<bool>.Success(ReferenceEquals(inner.Value, probe));
        }
    }

    private sealed class InnerCommandHandler(ScopeProbe probe) : ICommandHandler<InnerCommand, ScopeProbe>
    {
        public Task<Result<ScopeProbe>> Handle(InnerCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result<ScopeProbe>.Success(probe));
    }

    public sealed record WidgetCreatedDomainEvent : DomainEvent;

    private sealed class WidgetCreatedDomainEventHandler(List<string> journal) : IDomainEventHandler<WidgetCreatedDomainEvent>
    {
        public Task Handle(WidgetCreatedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Add("first");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondWidgetCreatedDomainEventHandler(List<string> journal) : IDomainEventHandler<WidgetCreatedDomainEvent>
    {
        public Task Handle(WidgetCreatedDomainEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Add("second");
            return Task.CompletedTask;
        }
    }

    public sealed record RecordingCommand : ICommand;

    private sealed class RecordingCommandHandler(List<string> journal) : ICommandHandler<RecordingCommand>
    {
        public Task<Result> Handle(RecordingCommand request, CancellationToken cancellationToken)
        {
            journal.Add("handler");
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class RecordingBehavior<TRequest, TResponse>(List<string> journal) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerContinuation<TResponse> next, CancellationToken cancellationToken)
        {
            journal.Add("behavior:before");
            var response = await next();
            journal.Add("behavior:after");
            return response;
        }
    }

    public sealed record UnhandledCommand : ICommand;

    /// <summary>
    /// A second handler for <see cref="CreateWidgetCommand"/>. Generic only so the assembly scan skips
    /// it; the duplicate-handler test registers a closed instance by hand.
    /// </summary>
    private sealed class AlternativeCreateWidgetHandler<TMarker> : IRequestHandler<CreateWidgetCommand, Result<Guid>>
    {
        public Task<Result<Guid>> Handle(CreateWidgetCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result<Guid>.Success(Guid.Empty));
    }

    private static ServiceProvider BuildProvider(List<string>? journal = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(journal ?? []);
        services.AddScoped<ScopeProbe>();
        services.AddSharedKernelMediatR(typeof(AddSharedKernelMediatRTests).Assembly);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task Send_Command_RunsItsHandler()
    {
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new CreateWidgetCommand("widget-1"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Send_Query_RunsANonPublicHandler()
    {
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new GetWidgetNameQuery(Guid.NewGuid()));

        result.Value.Should().Be("widget");
    }

    [Fact]
    public async Task CreateStream_YieldsTheHandlersRawItems()
    {
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var items = new List<int>();
        await foreach (var item in scope.ServiceProvider.GetRequiredService<ISender>().CreateStream(new ExportRowsStreamQuery(3)))
            items.Add(item);

        items.Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task Send_NestedFromAHandler_RunsInTheSameScope()
    {
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new OuterCommand());

        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task Send_RunsTheKernelPipelineBehaviorsAroundTheHandler()
    {
        var journal = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(journal);
        services.AddScoped<ScopeProbe>();
        services.AddSharedKernelMediatR(typeof(AddSharedKernelMediatRTests).Assembly);
        services.AddSharedKernelApplicationBehaviors()
            .AddBehavior(typeof(RecordingBehavior<,>), PipelineStage.Observability)
            .Build();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RecordingCommand());

        journal.Should().Equal("behavior:before", "handler", "behavior:after");
    }

    [Fact]
    public async Task Send_WithNoHandler_ThrowsNamingTheRequest()
    {
        await using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<ISender>().Send(new UnhandledCommand());

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*UnhandledCommand*");
    }

    [Fact]
    public async Task DomainEventDispatcher_RunsEveryScannedHandler()
    {
        var journal = new List<string>();
        await using var provider = BuildProvider(journal);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>()
            .DispatchAsync([new WidgetCreatedDomainEvent { OccurredOn = DateTimeOffset.UtcNow }], CancellationToken.None);

        journal.Should().BeEquivalentTo(["first", "second"]);
    }

    [Fact]
    public void AddSharedKernelMediatR_CalledTwice_RegistersEachHandlerOnce()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelMediatR(typeof(AddSharedKernelMediatRTests).Assembly);
        services.AddSharedKernelMediatR(typeof(AddSharedKernelMediatRTests).Assembly);

        services.Count(d => d.ServiceType == typeof(IRequestHandler<CreateWidgetCommand, Result<Guid>>)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IDomainEventHandler<WidgetCreatedDomainEvent>)).Should().Be(2);
        services.Count(d => d.ServiceType == typeof(ISender)).Should().Be(1);
    }

    [Fact]
    public void AddSharedKernelMediatR_WithNoAssembly_Throws()
    {
        var act = () => new ServiceCollection().AddSharedKernelMediatR();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddSharedKernelMediatR_WithASecondHandlerForTheSameRequest_Throws()
    {
        var services = new ServiceCollection();
        services.AddTransient<IRequestHandler<CreateWidgetCommand, Result<Guid>>, AlternativeCreateWidgetHandler<int>>();

        var act = () => services.AddSharedKernelMediatR(typeof(AddSharedKernelMediatRTests).Assembly);

        act.Should().Throw<InvalidOperationException>().WithMessage("*more than one handler*");
    }
}
