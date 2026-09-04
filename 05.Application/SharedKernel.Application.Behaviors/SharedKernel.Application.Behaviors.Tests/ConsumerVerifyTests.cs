using System.Diagnostics;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Polly;
using Polly.Registry;
using Polly.Retry;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.CacheInvalidation;
using SharedKernel.Application.Behaviors.Caching;
using SharedKernel.Application.Behaviors.DualApproval;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.FireAndForget;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Behaviors.Logging;
using SharedKernel.Application.Behaviors.Resilience;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests;

/// <summary>
/// Consumer-verify tests for <c>SharedKernel.Application.Behaviors</c> in isolation. Proves the
/// full eleven-behavior canonical pipeline — wired exactly as documented in <c>CLAUDE.md</c>'s "DI
/// Registration" section via a real <c>AddMediatR</c> + <c>ApplicationBehaviorsBuilder</c> — resolves
/// and executes end to end with zero exceptions, and that every missing-dependency
/// <c>Build()</c> guard throws <see cref="InvalidOperationException"/> exactly as documented.
/// Mirrors the pattern established by <c>07.Messaging</c>'s <c>ConsumerVerifyTests</c>.
/// </summary>
/// <remarks>
/// The original WO-035 seven-behavior coverage (Logging/Metrics/Validation/Authorization/
/// Caching/Idempotency/Transaction) below is unchanged. WO-036/WO-039/WO-040/WO-058 extensions —
/// Tracing, Resilience, CacheInvalidation, DualApproval, fire-and-forget dispatch, idempotency
/// response replay, <see cref="ApplicationBehaviorsBuilder.AddDefaultBehaviors"/>, and
/// <see cref="ILoggableRequest{TResponse}"/> — are covered further down this file (see the
/// "WO-036/WO-039/WO-040/WO-058 EXTENSIONS" region), so that collectively every one of the eleven
/// canonical pipeline slots is exercised at least once through a real, DI-registered pipeline.
/// </remarks>
public sealed class ConsumerVerifyTests
{
    private sealed record PlaceOrderCommand(string IdempotencyKey, Guid CustomerId)
        : ICommand<Guid>, IAuthorizeRequest, IIdempotentRequest
    {
        public string Requirement => "orders:create";
    }

    private sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, Guid>
    {
        public int InvocationCount { get; private set; }

        public Task<Result<Guid>> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(Result<Guid>.Success(Guid.NewGuid()));
        }
    }

    private sealed record GetOrderTotalQuery(Guid OrderId)
        : IQuery<decimal>, ICacheableQuery<Result<decimal>>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => $"orders:{OrderId:D}:total";
    }

    private sealed class GetOrderTotalQueryHandler : IQueryHandler<GetOrderTotalQuery, decimal>
    {
        public int InvocationCount { get; private set; }

        public Task<Result<decimal>> Handle(GetOrderTotalQuery request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(Result<decimal>.Success(42m));
        }
    }

    /// <summary>
    /// Builds the full opted-into seven-behavior pipeline against real fakes for every local seam,
    /// exactly as the "DI Registration" example in <c>CLAUDE.md</c> documents (minus the
    /// 06.Persistence/12.Security/07.Messaging bridges, which are simulated here by directly
    /// registering this package's own local seam interfaces).
    /// </summary>
    private static ServiceProvider BuildFullPipelineProvider(
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IAuthorizationContext authorizationContext,
        IIdempotencyKeyStore idempotencyKeyStore,
        PlaceOrderCommandHandler commandHandler,
        GetOrderTotalQueryHandler queryHandler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        services.AddSingleton(cacheService);
        services.AddSingleton(authorizationContext);
        services.AddSingleton(idempotencyKeyStore);
        services.AddSingleton(commandHandler);
        services.AddSingleton(queryHandler);
        services.AddSingleton<IRequestHandler<PlaceOrderCommand, Result<Guid>>>(
            sp => sp.GetRequiredService<PlaceOrderCommandHandler>());
        services.AddSingleton<IRequestHandler<GetOrderTotalQuery, Result<decimal>>>(
            sp => sp.GetRequiredService<GetOrderTotalQueryHandler>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ConsumerVerifyTests>());

        services
            .AddSharedKernelApplicationBehaviors()
            .AddLoggingBehavior()
            .AddMetricsBehavior()
            .AddValidationBehavior()
            .AddAuthorizationBehavior()
            .AddCachingBehavior()
            .AddIdempotencyBehavior()
            .AddTransactionBehavior()
            .Build();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void FullSevenBehaviorPipeline_Resolves_WithZeroExceptions()
    {
        var act = () => BuildFullPipelineProvider(
            Substitute.For<IUnitOfWork>(),
            Substitute.For<ICacheService>(),
            Substitute.For<IAuthorizationContext>(),
            Substitute.For<IIdempotencyKeyStore>(),
            new PlaceOrderCommandHandler(),
            new GetOrderTotalQueryHandler());

        act.Should().NotThrow();
    }

    [Fact]
    public async Task FullSevenBehaviorPipeline_AuthorizedIdempotentCommand_ExecutesHandlerAndCommitsTransaction()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var authorizationContext = Substitute.For<IAuthorizationContext>();
        authorizationContext.IsAuthorizedAsync("orders:create", Arg.Any<CancellationToken>()).Returns(true);
        var idempotencyKeyStore = Substitute.For<IIdempotencyKeyStore>();
        idempotencyKeyStore.HasProcessedAsync("order-1", Arg.Any<CancellationToken>()).Returns(false);
        var commandHandler = new PlaceOrderCommandHandler();

        using var provider = BuildFullPipelineProvider(
            unitOfWork,
            Substitute.For<ICacheService>(),
            authorizationContext,
            idempotencyKeyStore,
            commandHandler,
            new GetOrderTotalQueryHandler());
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new PlaceOrderCommand("order-1", Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        commandHandler.InvocationCount.Should().Be(1);
        await idempotencyKeyStore.Received(1).MarkProcessedAsync("order-1", Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FullSevenBehaviorPipeline_CacheableQuery_ExecutesHandlerAndCachesResult()
    {
        var cacheService = new RecordingCacheService();
        var queryHandler = new GetOrderTotalQueryHandler();

        using var provider = BuildFullPipelineProvider(
            Substitute.For<IUnitOfWork>(),
            cacheService,
            Substitute.For<IAuthorizationContext>(),
            Substitute.For<IIdempotencyKeyStore>(),
            new PlaceOrderCommandHandler(),
            queryHandler);
        var sender = provider.GetRequiredService<ISender>();
        var orderId = Guid.NewGuid();

        var first = await sender.Send(new GetOrderTotalQuery(orderId));
        var second = await sender.Send(new GetOrderTotalQuery(orderId));

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        queryHandler.InvocationCount.Should().Be(1, "the second call must be served from cache, not re-invoke the handler");
    }

    [Fact]
    public void Build_TransactionBehaviorWithoutIUnitOfWork_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddTransactionBehavior().Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IUnitOfWork*");
    }

    [Fact]
    public void Build_CachingBehaviorWithoutICacheService_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddCachingBehavior().Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ICacheService*");
    }

    [Fact]
    public void Build_AuthorizationBehaviorWithoutIAuthorizationContext_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddAuthorizationBehavior().Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IAuthorizationContext*");
    }

    [Fact]
    public void Build_IdempotencyBehaviorWithoutIIdempotencyKeyStore_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddIdempotencyBehavior().Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*IIdempotencyKeyStore*");
    }

    /// <summary>A minimal real (non-stampede-tested) <see cref="ICacheService"/> double for the end-to-end pipeline test.</summary>
    private sealed class RecordingCacheService : ICacheService
    {
        private readonly Dictionary<string, object?> _store = new();

        public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => ValueTask.FromResult(_store.TryGetValue(key, out var v) && v is T typed ? typed : default);

        public ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
        {
            _store[key] = value;
            return ValueTask.CompletedTask;
        }

        public async ValueTask<T> GetOrSetAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T>> factory,
            CachePolicy policy,
            CancellationToken ct = default)
        {
            if (_store.TryGetValue(key, out var existing) && existing is T typedExisting)
                return typedExisting;

            var value = await factory(ct).ConfigureAwait(false);
            _store[key] = value;
            return value;
        }

        public ValueTask RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyDictionary<string, T?>>(
                keys.ToDictionary(k => k, k => _store.TryGetValue(k, out var v) && v is T typed ? typed : default));

        public ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct = default)
        {
            foreach (var (k, v) in entries)
                _store[k] = v;
            return ValueTask.CompletedTask;
        }
    }

    // ================================================================================================
    // WO-036/WO-039/WO-040/WO-058 EXTENSIONS (05.Application/state-map.md Published phase, P-09/P-10/
    // P-14/P-18/P-25). Each group below proves one previously-untested slice of the canonical
    // eleven-step pipeline end to end through a real ServiceCollection + AddMediatR +
    // ApplicationBehaviorsBuilder chain — never a hand-rolled RequestHandlerDelegate<TResponse> mock.
    // ================================================================================================

    // ---- P-09 (part 1): a retryable + idempotent command that transiently fails then succeeds,
    // commits its transaction, and invalidates its declared cache key — Resilience -> Idempotency ->
    // Transaction -> CacheInvalidation all wired together via the real builder. ----

    private sealed record CreateAndCacheOrderCommand(string IdempotencyKey, Guid CustomerId)
        : ICommand<Guid>, IRetryableRequest, IIdempotentRequest, IInvalidatesCache
    {
        public IReadOnlyCollection<string> CacheKeysToInvalidate => [$"orders:{CustomerId:D}:total"];
    }

    private sealed class TransientThenSucceedsOrderHandler : ICommandHandler<CreateAndCacheOrderCommand, Guid>
    {
        private int _attempts;
        public int Attempts => _attempts;

        public Task<Result<Guid>> Handle(CreateAndCacheOrderCommand request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _attempts) < 3)
                throw new InvalidOperationException("transient failure");

            return Task.FromResult(Result<Guid>.Success(Guid.NewGuid()));
        }
    }

    private static ServiceProvider BuildResilientTransactionalPipelineProvider(
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        IIdempotencyKeyStore idempotencyKeyStore,
        TransientThenSucceedsOrderHandler handler,
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        services.AddSingleton(cacheService);
        services.AddSingleton(idempotencyKeyStore);
        services.AddSingleton(pipelineProvider);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<CreateAndCacheOrderCommand, Result<Guid>>>(
            sp => sp.GetRequiredService<TransientThenSucceedsOrderHandler>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ConsumerVerifyTests>());

        services
            .AddSharedKernelApplicationBehaviors()
            .AddLoggingBehavior()
            .AddMetricsBehavior()
            .AddTracingBehavior()
            .AddValidationBehavior()
            .AddResilienceBehavior()
            .AddIdempotencyBehavior()
            .AddTransactionBehavior()
            .AddCacheInvalidationBehavior()
            .Build();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task FullPipeline_RetryableIdempotentCommand_RetriesTransientFailure_ThenCommitsTransactionAndInvalidatesCache()
    {
        var registry = new ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder(
            ResilienceBehavior<CreateAndCacheOrderCommand, Result<Guid>>.DefaultPipelineKey,
            (builder, _) => builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = 5,
                Delay = TimeSpan.Zero,
            }));
        ResiliencePipelineProvider<string> pipelineProvider = registry;

        var unitOfWork = Substitute.For<IUnitOfWork>();
        var cacheService = Substitute.For<ICacheService>();
        var idempotencyKeyStore = Substitute.For<IIdempotencyKeyStore>();
        idempotencyKeyStore.HasProcessedAsync("order-retry-1", Arg.Any<CancellationToken>()).Returns(false);
        var handler = new TransientThenSucceedsOrderHandler();
        var customerId = Guid.NewGuid();

        using var provider = BuildResilientTransactionalPipelineProvider(
            unitOfWork, cacheService, idempotencyKeyStore, handler, pipelineProvider);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new CreateAndCacheOrderCommand("order-retry-1", customerId));

        result.IsSuccess.Should().BeTrue();
        handler.Attempts.Should().Be(3, "the resilience behavior must retry the transient failure twice before the handler succeeds");
        await idempotencyKeyStore.Received(1).MarkProcessedAsync("order-retry-1", Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await cacheService.Received(1).RemoveAsync($"orders:{customerId:D}:total", Arg.Any<CancellationToken>());
    }

    // ---- P-09 (part 2): a traced query produces exactly one recorded ActivitySource span through
    // the real DI-registered pipeline. ----

    [Fact]
    public async Task FullPipeline_TracedQuery_ProducesRecordedActivitySpan()
    {
        var capturedActivities = new List<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "SharedKernel.Application",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => capturedActivities.Add(activity),
        };
        ActivitySource.AddActivityListener(activityListener);

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IRequestHandler<GetOrderTotalQuery, Result<decimal>>, GetOrderTotalQueryHandler>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ConsumerVerifyTests>());
        services.AddSharedKernelApplicationBehaviors().AddLoggingBehavior().AddTracingBehavior().Build();

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        await sender.Send(new GetOrderTotalQuery(Guid.NewGuid()));

        var expectedName = typeof(GetOrderTotalQuery).FullName ?? nameof(GetOrderTotalQuery);
        capturedActivities.Should().ContainSingle(a => Equals(a.GetTagItem("request.name"), expectedName));
    }

    // ---- P-10: the Resilience and CacheInvalidation Build()-time missing-dependency guards. ----

    [Fact]
    public void Build_ResilienceBehaviorWithoutResiliencePipelineProvider_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddResilienceBehavior().Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ResiliencePipelineProvider*");
    }

    [Fact]
    public void Build_CacheInvalidationBehaviorWithoutICacheService_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddSharedKernelApplicationBehaviors().AddCacheInvalidationBehavior().Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ICacheService*");
    }

    // ---- P-14 (part 1): the fixed fire-and-forget dispatch path actually executes a handler end to
    // end through the real, documented AddFireAndForgetDispatch() wiring (WO-039, P-238). ----

    private sealed record NotifyCustomerCommand(string Id) : IFireAndForgetCommand;

    private sealed class NotifyCustomerCommandHandler(SemaphoreSlim gate) : IRequestHandler<NotifyCustomerCommand, Result>
    {
        public Task<Result> Handle(NotifyCustomerCommand request, CancellationToken cancellationToken)
        {
            gate.Release();
            return Task.FromResult(Result.Success());
        }
    }

    [Fact]
    public async Task FireAndForgetDispatch_EnqueuedCommand_ExecutesHandlerEndToEnd_ThroughRealDocumentedWiring()
    {
        var gate = new SemaphoreSlim(0, 1);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(gate);
        services.AddScoped<IRequestHandler<NotifyCustomerCommand, Result>>(
            sp => new NotifyCustomerCommandHandler(sp.GetRequiredService<SemaphoreSlim>()));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ConsumerVerifyTests>());
        services.AddSharedKernelApplicationBehaviors().AddFireAndForgetDispatch().Build();

        using var provider = services.BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<IFireAndForgetDispatcher>();
        var consumer = provider.GetRequiredService<IEnumerable<IHostedService>>()
            .OfType<FireAndForgetBackgroundConsumer>()
            .Single();

        await consumer.StartAsync(cts.Token);
        await dispatcher.EnqueueAsync(new NotifyCustomerCommand("cust-1"), cts.Token);

        var completed = await gate.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
        completed.Should().BeTrue(
            "the consumer-verify DI chain must actually execute the fire-and-forget handler end to end");

        await consumer.StopAsync(CancellationToken.None);
    }

    // ---- P-14 (part 2): a retried idempotent duplicate replays its original response when the
    // store opts in to IIdempotencyResponseStore (WO-039, P-242). ----

    private sealed class ReplayCapableIdempotencyStore : IIdempotencyKeyStore, IIdempotencyResponseStore
    {
        private readonly HashSet<string> _processed = [];
        private readonly Dictionary<string, string> _responses = [];

        public Task<bool> HasProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
            => Task.FromResult(_processed.Contains(idempotencyKey));

        public Task MarkProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            _processed.Add(idempotencyKey);
            return Task.CompletedTask;
        }

        public Task<string?> TryGetStoredResponseAsync(string idempotencyKey, CancellationToken cancellationToken)
            => Task.FromResult(_responses.TryGetValue(idempotencyKey, out var value) ? value : null);

        public Task StoreResponseAsync(string idempotencyKey, string serializedResponse, CancellationToken cancellationToken)
        {
            _responses[idempotencyKey] = serializedResponse;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task IdempotencyBehavior_DuplicateSubmission_ReplaysOriginalResponse_WhenStoreOptsIntoReplay()
    {
        var store = new ReplayCapableIdempotencyStore();
        var handler = new PlaceOrderCommandHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyKeyStore>(store);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<PlaceOrderCommand, Result<Guid>>>(
            sp => sp.GetRequiredService<PlaceOrderCommandHandler>());
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ConsumerVerifyTests>());
        services.AddSharedKernelApplicationBehaviors().AddIdempotencyBehavior().Build();

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var first = await sender.Send(new PlaceOrderCommand("replay-order", Guid.NewGuid()));
        var duplicate = await sender.Send(new PlaceOrderCommand("replay-order", Guid.NewGuid()));

        first.IsSuccess.Should().BeTrue();
        duplicate.IsSuccess.Should().BeTrue();
        duplicate.Value.Should().Be(first.Value, "a duplicate submission must replay the ORIGINAL stored response, not re-invoke the handler");
        handler.InvocationCount.Should().Be(1);
    }

    // ---- P-14 (part 3): AddDefaultBehaviors() resolves cleanly with zero missing-dependency
    // exceptions (WO-039, P-243). ----

    [Fact]
    public async Task AddDefaultBehaviors_ResolvesCleanly_WithZeroMissingDependencyExceptions()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IRequestHandler<GetOrderTotalQuery, Result<decimal>>, GetOrderTotalQueryHandler>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ConsumerVerifyTests>());

        var act = () => services.AddSharedKernelApplicationBehaviors().AddDefaultBehaviors().Build();

        act.Should().NotThrow();

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();
        var result = await sender.Send(new GetOrderTotalQuery(Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
    }

    // ---- P-18: a request implementing ILoggableRequest<TResponse> logs both request and response
    // structured fields end to end through the real DI-registered pipeline (WO-040, P-246). ----

    // Public (not private-nested), because NSubstitute must generate a Castle dynamic proxy for
    // ILogger<LoggableGetOrderTotalQuery> below — an inaccessible generic type argument makes proxy
    // generation fail, mirroring why LoggingBehaviorLoggableRequestTests.cs declares its own
    // ILoggableRequest<TResponse> test types at file scope rather than as private nested classes.
    public sealed class LoggableGetOrderTotalQuery(Guid orderId) : IQuery<decimal>, ILoggableRequest<Result<decimal>>
    {
        public Guid OrderId { get; } = orderId;

        public IReadOnlyDictionary<string, object?> LoggableRequestFields =>
            new Dictionary<string, object?> { ["OrderId"] = OrderId };

        public IReadOnlyDictionary<string, object?>? GetLoggableResponseFields(Result<decimal> response) =>
            new Dictionary<string, object?> { ["Total"] = response.IsSuccess ? response.Value : null };
    }

    private sealed class LoggableGetOrderTotalQueryHandler : IRequestHandler<LoggableGetOrderTotalQuery, Result<decimal>>
    {
        public Task<Result<decimal>> Handle(LoggableGetOrderTotalQuery request, CancellationToken cancellationToken)
            => Task.FromResult(Result<decimal>.Success(99m));
    }

    [Fact]
    public async Task LoggingBehavior_LoggableRequest_LogsRequestAndResponseFields_ThroughRealDIRegisteredPipeline()
    {
        var logger = Substitute.For<ILogger<LoggableGetOrderTotalQuery>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddSingleton<IRequestHandler<LoggableGetOrderTotalQuery, Result<decimal>>, LoggableGetOrderTotalQueryHandler>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ConsumerVerifyTests>());
        services.AddSharedKernelApplicationBehaviors().AddLoggingBehavior().Build();

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();
        var orderId = Guid.NewGuid();

        await sender.Send(new LoggableGetOrderTotalQuery(orderId));

        logger.Received(1).BeginScope(
            Arg.Is<IReadOnlyDictionary<string, object?>>(d => Equals(d["OrderId"], orderId)));
        logger.Received(1).BeginScope(
            Arg.Is<IReadOnlyDictionary<string, object?>>(d => Equals(d["Total"], 99m)));
    }

    // ---- P-25: the maker-checker flow end to end through the real DI-registered pipeline
    // (WO-058, P-380) — unapproved dispatch fails with ErrorType.Forbidden, self-approval fails with
    // ErrorType.Forbidden, a distinct-approver-recorded dispatch succeeds. ----

    private sealed record ApproveOrRejectRefundCommand(string ApprovalKey, Guid RefundId)
        : ICommand<Guid>, IRequiresDualApproval;

    private sealed class ApproveOrRejectRefundCommandHandler : IRequestHandler<ApproveOrRejectRefundCommand, Result<Guid>>
    {
        public int InvocationCount { get; private set; }

        public Task<Result<Guid>> Handle(ApproveOrRejectRefundCommand request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(Result<Guid>.Success(request.RefundId));
        }
    }

    private static IAuthorizationContext CreateIdentityCapableContext(string currentIdentity)
    {
        var context = Substitute.For<IAuthorizationContext, IAuthorizationContextIdentity>();
        ((IAuthorizationContextIdentity)context).GetCurrentIdentityAsync(Arg.Any<CancellationToken>())
            .Returns(currentIdentity);
        return context;
    }

    private static ServiceProvider BuildDualApprovalPipelineProvider(
        IAuthorizationContext authorizationContext,
        IDualApprovalStore dualApprovalStore,
        ApproveOrRejectRefundCommandHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authorizationContext);
        services.AddSingleton(dualApprovalStore);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<ApproveOrRejectRefundCommand, Result<Guid>>>(
            sp => sp.GetRequiredService<ApproveOrRejectRefundCommandHandler>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ConsumerVerifyTests>());
        services.AddSharedKernelApplicationBehaviors().AddLoggingBehavior().AddDualApprovalBehavior().Build();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DualApprovalBehavior_UnapprovedDispatch_FailsWithForbidden_ThroughRealDIRegisteredPipeline()
    {
        var authContext = CreateIdentityCapableContext("maker");
        var store = Substitute.For<IDualApprovalStore>();
        store.TryGetApprovalAsync("refund-1", Arg.Any<CancellationToken>()).Returns((string?)null);
        var handler = new ApproveOrRejectRefundCommandHandler();

        using var provider = BuildDualApprovalPipelineProvider(authContext, store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new ApproveOrRejectRefundCommand("refund-1", Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        handler.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task DualApprovalBehavior_SelfApprovalDispatch_FailsWithForbidden_ThroughRealDIRegisteredPipeline()
    {
        var authContext = CreateIdentityCapableContext("maker");
        var store = Substitute.For<IDualApprovalStore>();
        store.TryGetApprovalAsync("refund-2", Arg.Any<CancellationToken>()).Returns("maker");
        var handler = new ApproveOrRejectRefundCommandHandler();

        using var provider = BuildDualApprovalPipelineProvider(authContext, store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new ApproveOrRejectRefundCommand("refund-2", Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        handler.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task DualApprovalBehavior_DistinctApproverRecordedDispatch_Succeeds_ThroughRealDIRegisteredPipeline()
    {
        var authContext = CreateIdentityCapableContext("maker");
        var store = Substitute.For<IDualApprovalStore>();
        store.TryGetApprovalAsync("refund-3", Arg.Any<CancellationToken>()).Returns("checker");
        var handler = new ApproveOrRejectRefundCommandHandler();

        using var provider = BuildDualApprovalPipelineProvider(authContext, store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new ApproveOrRejectRefundCommand("refund-3", Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        handler.InvocationCount.Should().Be(1);
    }

    // ---- P-29 (WO-071): the auditing flow end to end through the real DI-registered pipeline — a
    // plain audited command records exactly one entry via a spy IAuditTrailWriter; a dual-approval-
    // linked audited command's recorded entry carries the approval key; a non-audited command
    // triggers zero calls to the seam. ----

    private sealed record UpdateCustomerAddressCommand(Guid CustomerId, string NewAddress, string OldAddress)
        : ICommand, IAuditableRequest<Result>
    {
        public string Action => "customer.address.update";
        public string ResourceType => "Customer";
        public string ResourceId => CustomerId.ToString("D");
        public string? BeforeSnapshot => OldAddress;
        public string? GetAfterSnapshot(Result response) => response.IsSuccess ? NewAddress : null;
    }

    private sealed class UpdateCustomerAddressCommandHandler : IRequestHandler<UpdateCustomerAddressCommand, Result>
    {
        public Task<Result> Handle(UpdateCustomerAddressCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed record RotateSigningKeyCommand(Guid KeyId)
        : ICommand, IRequiresDualApproval, IAuditableRequest<Result>
    {
        public string ApprovalKey => $"rotate-signing-key:{KeyId:D}";
        public string Action => "signing-key.rotate";
        public string ResourceType => "SigningKey";
        public string ResourceId => KeyId.ToString("D");
        public string? BeforeSnapshot => null;
        public string? GetAfterSnapshot(Result response) => response.IsSuccess ? "rotated" : "rejected";
    }

    private sealed class RotateSigningKeyCommandHandler : IRequestHandler<RotateSigningKeyCommand, Result>
    {
        public Task<Result> Handle(RotateSigningKeyCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed record PlainNonAuditedCommand : ICommand;

    private sealed class PlainNonAuditedCommandHandler : IRequestHandler<PlainNonAuditedCommand, Result>
    {
        public Task<Result> Handle(PlainNonAuditedCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private static ServiceProvider BuildAuditingPipelineProvider(
        IAuditTrailWriter auditTrailWriter,
        IUnitOfWork unitOfWork,
        IAuthorizationContext? authorizationContext,
        IDualApprovalStore? dualApprovalStore)
    {
        var services = new ServiceCollection();
        services.AddSingleton(auditTrailWriter);
        services.AddSingleton(unitOfWork);
        if (authorizationContext is not null)
            services.AddSingleton(authorizationContext);
        if (dualApprovalStore is not null)
            services.AddSingleton(dualApprovalStore);
        services.AddSingleton<UpdateCustomerAddressCommandHandler>();
        services.AddSingleton<IRequestHandler<UpdateCustomerAddressCommand, Result>>(
            sp => sp.GetRequiredService<UpdateCustomerAddressCommandHandler>());
        services.AddSingleton<RotateSigningKeyCommandHandler>();
        services.AddSingleton<IRequestHandler<RotateSigningKeyCommand, Result>>(
            sp => sp.GetRequiredService<RotateSigningKeyCommandHandler>());
        services.AddSingleton<PlainNonAuditedCommandHandler>();
        services.AddSingleton<IRequestHandler<PlainNonAuditedCommand, Result>>(
            sp => sp.GetRequiredService<PlainNonAuditedCommandHandler>());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ConsumerVerifyTests>());

        var builder = services.AddSharedKernelApplicationBehaviors()
            .AddLoggingBehavior()
            .AddAuditingBehavior()
            .AddTransactionBehavior();
        if (authorizationContext is not null && dualApprovalStore is not null)
            builder.AddDualApprovalBehavior();
        builder.Build();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AuditingBehavior_PlainAuditedCommand_RecordsExactlyOneEntry_ThroughRealDIRegisteredPipeline()
    {
        var writer = Substitute.For<IAuditTrailWriter>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        using var provider = BuildAuditingPipelineProvider(writer, unitOfWork, null, null);
        var sender = provider.GetRequiredService<ISender>();
        var customerId = Guid.NewGuid();

        var result = await sender.Send(new UpdateCustomerAddressCommand(customerId, "new-address", "old-address"));

        result.IsSuccess.Should().BeTrue();
        await writer.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e =>
                e.Action == "customer.address.update" &&
                e.ResourceId == customerId.ToString("D") &&
                e.AfterSnapshot == "new-address" &&
                e.ApprovalId == null),
            Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuditingBehavior_DualApprovalLinkedCommand_RecordedEntryCarriesApprovalKey_ThroughRealDIRegisteredPipeline()
    {
        var writer = Substitute.For<IAuditTrailWriter>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var authContext = CreateIdentityCapableContext("maker");
        var keyId = Guid.NewGuid();
        var approvalKey = $"rotate-signing-key:{keyId:D}";
        var store = Substitute.For<IDualApprovalStore>();
        store.TryGetApprovalAsync(approvalKey, Arg.Any<CancellationToken>()).Returns("checker");

        using var provider = BuildAuditingPipelineProvider(writer, unitOfWork, authContext, store);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new RotateSigningKeyCommand(keyId));

        result.IsSuccess.Should().BeTrue();
        await writer.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e => e.ApprovalId == approvalKey),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuditingBehavior_NonAuditedCommand_TriggersZeroCallsToTheSeam_ThroughRealDIRegisteredPipeline()
    {
        var writer = Substitute.For<IAuditTrailWriter>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        using var provider = BuildAuditingPipelineProvider(writer, unitOfWork, null, null);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new PlainNonAuditedCommand());

        result.IsSuccess.Should().BeTrue();
        await writer.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
