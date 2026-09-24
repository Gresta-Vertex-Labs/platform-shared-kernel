using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MsOptions = Microsoft.Extensions.Options.Options;
using SharedKernel.Application;
using SharedKernel.Application.Idempotency;
using SharedKernel.Application.Context;
using SharedKernel.Idempotency.Redis.KeyStore;
using SharedKernel.Idempotency.Redis.Options;
using SharedKernel.Messaging.Abstractions.TenantContext;
using SharedKernel.Persistence.Testing;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Concurrency;

/// <summary>
/// <c>05.Application</c>'s <see cref="IdempotencyBehavior{TRequest,TResponse}"/> over the real Redis store: reservations
/// are scoped per tenant and caller (P-562 X3), and the Redis key ends in a fixed-length digest rather than the
/// command's own key.
/// </summary>
/// <remarks>REQUIRES A DOCKER DAEMON (Testcontainers Redis).</remarks>
[Collection("RedisContainer")]
public sealed class RedisIdempotencyCallerScopingTests(RedisContainerFixture fixture)
{
    private sealed record PlaceOrder(string IdempotencyKey, string Body) : ICommand<string>, IIdempotentRequest;

    private sealed class FixedTenantAccessor(Guid? tenantId) : ITenantContextAccessor
    {
        public Guid? TenantId { get; } = tenantId;
    }

    private static async Task<Result<string>> SendAs(
        IConnectionMultiplexer multiplexer,
        TestRequestContext caller,
        PlaceOrder command,
        RequestHandlerDelegate<Result<string>> handler)
    {
        var store = new RedisRequestIdempotencyStore(
            multiplexer,
            new FixedTenantAccessor(caller.TenantId),
            MsOptions.Create(new RedisIdempotencyOptions()),
            new InMemoryLogger<RedisRequestIdempotencyStore>());
        // The behavior is internal to SharedKernel.Application: compose it the way a service does.
        var services = new ServiceCollection();
        services.AddSingleton<IRequestIdempotencyStore>(store);
        services.AddSingleton<IRequestContext>(caller);
        services.AddSharedKernelApplication(typeof(RedisIdempotencyCallerScopingTests).Assembly, app => app.WithIdempotency());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var behavior = scope.ServiceProvider.GetServices<IPipelineBehavior<PlaceOrder, Result<string>>>()
            .Single(b => b.GetType().Name.StartsWith("IdempotencyBehavior", StringComparison.Ordinal));

        return await behavior.Handle(command, handler, CancellationToken.None);
    }

    private static List<string> StoredKeysOf(IConnectionMultiplexer multiplexer, Guid tenantId)
    {
        var server = multiplexer.GetServer(multiplexer.GetEndPoints()[0]);
        return server.Keys(pattern: $"sk:idempotency:{tenantId:D}:key:*").Select(key => key.ToString()).ToList();
    }

    [Fact]
    public async Task Behavior_TwoUsersOfOneTenant_SameKeyAndBody_EachGetsTheirOwnExecution_AndEachRetryReplaysItsOwn()
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var alice = TestRequestContext.ForTenant(tenantId, "alice");
        var bob = TestRequestContext.ForTenant(tenantId, "bob");
        var command = new PlaceOrder($"order-{Guid.NewGuid():N}", "identical body");
        var executions = 0;

        RequestHandlerDelegate<Result<string>> HandlerFor(string owner) => () =>
        {
            executions++;
            return Task.FromResult(Result<string>.Success($"{owner}'s one-time secret"));
        };

        var aliceFirst = await SendAs(multiplexer, alice, command, HandlerFor("alice"));
        var bobFirst = await SendAs(multiplexer, bob, command, HandlerFor("bob"));
        var aliceRetry = await SendAs(multiplexer, alice, command, HandlerFor("alice (rerun)"));
        var bobRetry = await SendAs(multiplexer, bob, command, HandlerFor("bob (rerun)"));

        Assert.Equal(2, executions);
        Assert.Equal("alice's one-time secret", aliceFirst.Value);
        Assert.Equal("bob's one-time secret", bobFirst.Value);
        Assert.Equal("alice's one-time secret", aliceRetry.Value);
        Assert.Equal("bob's one-time secret", bobRetry.Value);

        var keys = StoredKeysOf(multiplexer, tenantId);
        Assert.Equal(2, keys.Distinct().Count());
        Assert.All(keys, key => Assert.Matches($"^sk:idempotency:{tenantId:D}:key:[0-9a-f]{{64}}$", key));
    }

    /// <summary>
    /// The documented residual risk, against the real store: anonymous callers of one tenant share one scope, where
    /// only the fingerprint separates them.
    /// </summary>
    [Fact]
    public async Task Behavior_AnonymousCallersOfOneTenant_ReplayOnTheSameBody_AndConflictOnAnother()
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var key = $"anonymous-{Guid.NewGuid():N}";
        var executions = 0;

        RequestHandlerDelegate<Result<string>> Handler(string response) => () =>
        {
            executions++;
            return Task.FromResult(Result<string>.Success(response));
        };

        await SendAs(multiplexer, TestRequestContext.Anonymous(tenantId), new PlaceOrder(key, "body"), Handler("first sender"));
        var sameBody = await SendAs(multiplexer, TestRequestContext.Anonymous(tenantId), new PlaceOrder(key, "body"), Handler("second sender"));
        var otherBody = await SendAs(multiplexer, TestRequestContext.Anonymous(tenantId), new PlaceOrder(key, "other body"), Handler("third sender"));

        Assert.Equal(1, executions);
        Assert.Equal("first sender", sameBody.Value);
        Assert.Equal("idempotency.key_reused", otherBody.Error.Code);
    }
}
