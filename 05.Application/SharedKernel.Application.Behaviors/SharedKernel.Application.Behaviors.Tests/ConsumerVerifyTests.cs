using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.Caching;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Behaviors.Transaction;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests;

/// <summary>
/// Consumer-verify tests for <c>SharedKernel.Application.Behaviors</c> in isolation. Proves the
/// full seven-behavior pipeline — wired exactly as documented in <c>CLAUDE.md</c>'s "DI
/// Registration" section via a real <c>AddMediatR</c> + <c>ApplicationBehaviorsBuilder</c> — resolves
/// and executes end to end with zero exceptions, and that each of the four missing-dependency
/// <c>Build()</c> guards throws <see cref="InvalidOperationException"/> exactly as documented.
/// Mirrors the pattern established by <c>07.Messaging</c>'s <c>ConsumerVerifyTests</c>.
/// </summary>
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
}
