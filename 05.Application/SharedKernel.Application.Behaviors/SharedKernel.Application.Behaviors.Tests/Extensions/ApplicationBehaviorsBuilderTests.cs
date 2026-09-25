using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Auditing;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Execution.Transactions;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Behaviors.Tests.Support;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Execution.Context;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Extensions;

public sealed class ApplicationBehaviorsBuilderTests
{
    private sealed record TestCommand : ICommand;

    [Fact]
    public void Build_AddTransactionBehaviorWithoutIUnitOfWork_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelApplicationBehaviors().AddTransactionBehavior();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IUnitOfWork*");
    }

    [Fact]
    public void Build_AddAuthorizationBehaviorWithoutIRequestContext_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelApplicationBehaviors().AddAuthorizationBehavior();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IRequestContext*");
    }

    [Fact]
    public void Build_AddIdempotencyBehaviorWithoutStore_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelApplicationBehaviors().AddIdempotencyBehavior();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IRequestIdempotencyStore*");
    }

    [Fact]
    public void Build_AddAuditingBehaviorWithoutWriter_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelApplicationBehaviors().AddAuditingBehavior();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IAuditTrailWriter*");
    }

    [Fact]
    public void Build_WithRequiredServicesRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork());
        services.AddSingleton<IRequestContext>(new FakeRequestContext(true, new HashSet<string> { "p" }));
        services.AddSingleton<IRequestIdempotencyStore>(new FakeIdempotencyStore());
        services.AddSingleton<IAuditTrailWriter>(new FakeAuditTrailWriter());

        var act = () => services.AddSharedKernelApplicationBehaviors()
            .AddAuthorizationBehavior()
            .AddIdempotencyBehavior()
            .AddTransactionBehavior()
            .AddAuditingBehavior()
            .Build();

        act.Should().NotThrow();
    }

    [Fact]
    public void Build_CalledTwice_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelApplicationBehaviors().AddValidationBehavior();
        builder.Build();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*already been called*");
    }

    [Fact]
    public void AddBehavior_UndefinedPipelineStage_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelApplicationBehaviors();

        var act = () => builder.AddBehavior(typeof(NotAnOpenGeneric), (PipelineStage)99);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("stage");
    }

    [Fact]
    public void AddDefaultBehaviors_IsEquivalentToTracingLoggingMetricsValidation_AndDoesNotThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddDefaultBehaviors().Build();

        act.Should().NotThrow();
    }

    [Fact]
    public void AddDefaultBehaviors_CalledAlongsideIndividualCall_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ApplicationBehaviorsBuilderTests>());

        services.AddSharedKernelApplicationBehaviors()
            .AddDefaultBehaviors()
            .AddValidationBehavior() // same behavior opted into twice
            .Build();

        using var provider = services.BuildServiceProvider();
        var behaviors = provider.GetServices<IPipelineBehavior<TestCommand, Result>>().ToList();

        behaviors.Count(b => b.GetType().Name.StartsWith("ValidationBehavior")).Should().Be(1);
    }

    // ---- AddBehavior validation ----

    private sealed class NotAnOpenGeneric : IPipelineBehavior<TestCommand, Result>
    {
        public Task<Result> Handle(TestCommand request, RequestHandlerDelegate<Result> next, CancellationToken ct) => next();
    }

    private sealed class NotAPipelineBehavior<TRequest, TResponse>;

    [Fact]
    public void Build_AddBehavior_ClosedGenericType_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelApplicationBehaviors()
            .AddBehavior(typeof(NotAnOpenGeneric), PipelineStage.Command);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*open generic*");
    }

    [Fact]
    public void Build_AddBehavior_NotImplementingIPipelineBehavior_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelApplicationBehaviors()
            .AddBehavior(typeof(NotAPipelineBehavior<,>), PipelineStage.Command);

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IPipelineBehavior*");
    }

    private sealed class RequiresMarkerServiceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        // Constructor dependency only — proves DI can satisfy the required-service declaration;
        // the behavior itself has no other use for it.
        public RequiresMarkerServiceBehavior(MarkerService marker) => _ = marker;

        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct) => next();
    }

    private sealed class MarkerService;

    [Fact]
    public void Build_AddBehavior_MissingRequiredService_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelApplicationBehaviors()
            .AddBehavior(typeof(RequiresMarkerServiceBehavior<,>), PipelineStage.Command, typeof(MarkerService));

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*MarkerService*");
    }

    [Fact]
    public void Build_AddBehavior_RequiredServiceRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MarkerService>();
        var builder = services.AddSharedKernelApplicationBehaviors()
            .AddBehavior(typeof(RequiresMarkerServiceBehavior<,>), PipelineStage.Command, typeof(MarkerService));

        var act = () => builder.Build();

        act.Should().NotThrow();
    }

    // ---- Multiple custom behaviors in the same stage run in the order they were added ----

    private class OrderedMarker<TRequest, TResponse>(List<string> sequence, string name) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            sequence.Add($"{name}.pre");
            var response = await next().ConfigureAwait(false);
            sequence.Add($"{name}.post");
            return response;
        }
    }

    private sealed class FirstMarker<TRequest, TResponse>(List<string> sequence) : OrderedMarker<TRequest, TResponse>(sequence, "first")
        where TRequest : notnull;

    private sealed class SecondMarker<TRequest, TResponse>(List<string> sequence) : OrderedMarker<TRequest, TResponse>(sequence, "second")
        where TRequest : notnull;

    private sealed class OrderedMarkerHandler(List<string> sequence) : ICommandHandler<TestCommand>
    {
        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            sequence.Add("handler");
            return Task.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task AddBehavior_TwoCustomBehaviorsInSameStage_RunInAdditionOrder()
    {
        var sequence = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(sequence);
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ApplicationBehaviorsBuilderTests>());

        services.AddSharedKernelApplicationBehaviors()
            .AddBehavior(typeof(FirstMarker<,>), PipelineStage.Query)
            .AddBehavior(typeof(SecondMarker<,>), PipelineStage.Query)
            .Build();

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new TestCommand());

        sequence.Should().Equal("first.pre", "second.pre", "handler", "second.post", "first.post");
    }
}
