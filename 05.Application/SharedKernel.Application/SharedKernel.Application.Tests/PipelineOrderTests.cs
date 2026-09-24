using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Transactions;
using SharedKernel.Application.Tests.Support;
using SharedKernel.Application.Context;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Tests;

/// <summary>
/// Proves the fixed canonical five-stage pipeline order, custom <see cref="PipelineStage"/>
/// placement via <see cref="ApplicationPipelineBuilder.WithBehavior"/>, and
/// <see cref="ICommandScope"/>'s post-commit callback timing — all through one real, composed
/// <c>ServiceCollection</c> + <c>AddSharedKernelApplication</c> dispatch,
/// never a hand-rolled <see cref="RequestHandlerDelegate{TResponse}"/> mock.
/// </summary>
public sealed class PipelineOrderTests
{
    [RequirePermission("test.permission")]
    private sealed record TestCommand(string IdempotencyKey) : ICommand<string>, IIdempotentRequest, IAuditableRequest<Result<string>>
    {
        public string Action => "test.action";
        public string ResourceType => "TestResource";
        public string ResourceId => "r-1";
        public string? BeforeSnapshot => null;
        public string? GetAfterSnapshot(Result<string> response) => response.IsSuccess ? response.Value : null;
    }

    private sealed class TestCommandHandler(List<string> sequence) : ICommandHandler<TestCommand, string>
    {
        public Task<Result<string>> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            sequence.Add("handler");
            return Task.FromResult(Result<string>.Success("ok"));
        }
    }

    // ---- Stage markers: each records "{name}.pre" before next() and "{name}.post" after. ----

    private sealed class ObservabilityMarker<TRequest, TResponse>(List<string> sequence) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            sequence.Add("observability.pre");
            var response = await next().ConfigureAwait(false);
            sequence.Add("observability.post");
            return response;
        }
    }

    private sealed class AuthorizationMarker<TRequest, TResponse>(List<string> sequence) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            sequence.Add("authorization.pre");
            var response = await next().ConfigureAwait(false);
            sequence.Add("authorization.post");
            return response;
        }
    }

    private sealed class ValidationMarker<TRequest, TResponse>(List<string> sequence) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            sequence.Add("validation.pre");
            var response = await next().ConfigureAwait(false);
            sequence.Add("validation.post");
            return response;
        }
    }

    private sealed class QueryMarker<TRequest, TResponse>(List<string> sequence) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            sequence.Add("query.pre");
            var response = await next().ConfigureAwait(false);
            sequence.Add("query.post");
            return response;
        }
    }

    /// <summary>
    /// The custom Command-stage marker — registered innermost among the command-stage behaviors,
    /// and itself registers a post-commit callback via <see cref="ICommandScope"/>, mirroring how
    /// <c>SharedKernel.Application.Caching</c>'s <c>CacheInvalidationBehavior</c> defers
    /// its own work to <see cref="ICommandScope.OnCompleted"/> instead of evicting directly.
    /// </summary>
    private sealed class CommandMarker<TRequest, TResponse>(List<string> sequence, ICommandScope scope) : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
        {
            sequence.Add("command.pre");
            var response = await next().ConfigureAwait(false);
            sequence.Add("command.post");
            scope.OnCompleted(_ =>
            {
                sequence.Add("command.oncompleted");
                return Task.CompletedTask;
            });
            return response;
        }
    }

    private static ServiceProvider BuildProvider(List<string> sequence)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(sequence);
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork(sequence));
        services.AddSingleton<IRequestIdempotencyStore>(new FakeIdempotencyStore(sequence));
        services.AddSingleton<IAuditTrailWriter>(new FakeAuditTrailWriter(sequence));
        services.AddSingleton<IRequestContext>(new FakeRequestContext(isAuthenticated: true, new HashSet<string> { "test.permission" }));

        // Opt-ins deliberately listed out of canonical order: the order must come from the package.
        services.AddSharedKernelApplication(typeof(PipelineOrderTests).Assembly, app => app
            .WithBehavior(typeof(CommandMarker<,>), PipelineStage.Command)
            .WithAuditing()
            .WithTransactions()
            .WithBehavior(typeof(QueryMarker<,>), PipelineStage.Query)
            .WithIdempotency()
            .WithBehavior(typeof(ValidationMarker<,>), PipelineStage.Validation)
            .WithBehavior(typeof(AuthorizationMarker<,>), PipelineStage.Authorization)
            .WithBehavior(typeof(ObservabilityMarker<,>), PipelineStage.Observability));

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Dispatch_RealComposedPipeline_ExecutesInFixedCanonicalFiveStageOrder()
    {
        var sequence = new List<string>();
        using var provider = BuildProvider(sequence);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand("key-1"));

        result.IsSuccess.Should().BeTrue();

        // Pre-next code runs outermost-first: Observability -> Authorization -> Validation ->
        // Query -> Command -> handler.
        var preNextOrder = sequence.Where(e => e is "observability.pre" or "authorization.pre" or "validation.pre" or "query.pre" or "command.pre" or "handler").ToList();
        preNextOrder.Should().Equal(
            "observability.pre", "authorization.pre", "validation.pre", "query.pre", "command.pre", "handler");

        // Post-next code inside the command stage runs innermost-first: the custom Command-stage
        // marker, then Auditing, then Transaction's commit, then Idempotency's completion — and
        // ICommandScope.OnCompleted's queued callback fires only after ALL of that, last of all.
        var commandStagePostOrder = sequence.Where(e =>
            e is "command.post" or "audit.record" or "transaction.commit" or "idempotency.complete" or "command.oncompleted").ToList();
        commandStagePostOrder.Should().Equal(
            "command.post", "audit.record", "transaction.commit", "idempotency.complete", "command.oncompleted");

        // The outer stages unwind after the command stage has fully completed, in reverse order.
        var outerPostOrder = sequence.Where(e => e is "query.post" or "validation.post" or "authorization.post" or "observability.post").ToList();
        outerPostOrder.Should().Equal("query.post", "validation.post", "authorization.post", "observability.post");

        // command.oncompleted must be the very last thing to happen before the outer stages unwind.
        sequence.IndexOf("command.oncompleted").Should().BeLessThan(sequence.IndexOf("query.post"));
    }

    /// <summary>
    /// <c>AddSharedKernelApplication</c> registers logger-dependent behaviors
    /// (<c>CommandScopeBehavior</c>, <c>IdempotencyBehavior</c>) that constructor-inject
    /// <c>ILogger&lt;T&gt;</c>. Proves the registration call itself makes logging resolvable — a bare
    /// <see cref="ServiceCollection"/> with no prior <c>services.AddLogging()</c> call must still
    /// dispatch successfully once idempotency and transaction are both opted into.
    /// </summary>
    [Fact]
    public async Task AddSharedKernelApplication_WithIdempotencyAndTransaction_NoPriorAddLogging_DispatchesSuccessfully()
    {
        var services = new ServiceCollection();
        var sequence = new List<string>();
        services.AddSingleton(sequence);
        services.AddSingleton<IUnitOfWork>(new FakeUnitOfWork(sequence));
        services.AddSingleton<IRequestIdempotencyStore>(new FakeIdempotencyStore(sequence));
        services.AddSingleton<IRequestContext>(new FakeRequestContext(isAuthenticated: true, new HashSet<string> { "test.permission" }));

        // Deliberately no services.AddLogging() call anywhere above — the registration call must add it itself.
        services.AddSharedKernelApplication(typeof(PipelineOrderTests).Assembly, app => app
            .WithIdempotency()
            .WithTransactions());

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand("key-no-prior-logging"));

        result.IsSuccess.Should().BeTrue();
        sequence.Should().Contain("idempotency.complete");
        sequence.Should().Contain("transaction.commit");
    }

    private static List<Type> RegisteredBehaviors(IServiceCollection services)
        => services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType!)
            .ToList();

    /// <summary>
    /// Authorization is always on: with no opt-in at all, it sits between the observability
    /// behaviors and validation, so a <see cref="RequirePermissionAttribute"/> can never go
    /// unenforced because a registration call was forgotten.
    /// </summary>
    [Fact]
    public void Register_NoOptIns_AuthorizationIsAlwaysRegisteredBetweenObservabilityAndValidation()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(typeof(PipelineOrderTests).Assembly);

        RegisteredBehaviors(services).Should().Equal(
            typeof(TracingBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(MetricsBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(ValidationBehavior<,>));
    }

    [Fact]
    public void Register_EveryOptIn_AuthorizationKeepsItsCanonicalPositionOnce()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(typeof(PipelineOrderTests).Assembly, app => app
            .WithAuditing()
            .WithTransactions()
            .WithIdempotency()
            .WithBehavior(typeof(AuthorizationMarker<,>), PipelineStage.Authorization));

        RegisteredBehaviors(services).Should().Equal(
            typeof(TracingBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(MetricsBehavior<,>),
            typeof(AuthorizationBehavior<,>),
            typeof(AuthorizationMarker<,>),
            typeof(ValidationBehavior<,>),
            typeof(CommandScopeBehavior<,>),
            typeof(IdempotencyBehavior<,>),
            typeof(AuditingBehavior<,>),
            typeof(TransactionBehavior<,>),
            typeof(AuditingCommitBehavior<,>));
    }
}
