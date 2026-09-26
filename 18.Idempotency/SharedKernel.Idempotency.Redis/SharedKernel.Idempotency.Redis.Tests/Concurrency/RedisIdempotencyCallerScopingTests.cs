using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Idempotency;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Idempotency.Redis.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Execution;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Concurrency;

/// <summary>
/// <c>05.Application</c>'s <c>IdempotencyBehavior</c> over the real Redis store, registered the way a service registers
/// it: reservations are scoped per tenant and caller (P-562 X3, merged in P-579), and the Redis key ends in a
/// fixed-length digest rather than the command's own key.
/// </summary>
/// <remarks>REQUIRES A DOCKER DAEMON (Testcontainers Redis).</remarks>
[Collection("RedisContainer")]
public sealed class RedisIdempotencyCallerScopingTests(RedisContainerFixture fixture)
{
    private sealed record PlaceOrder(string IdempotencyKey, string Body) : ICommand<string>, IIdempotentRequest;

    /// <summary>
    /// Sends <paramref name="command"/> through the idempotency behavior as <paramref name="caller"/>. The caller is both
    /// the request context the behavior scopes the key by and the ambient context the store takes its tenant from.
    /// </summary>
    private async Task<Result<string>> SendAs(
        TestRequestContext caller,
        PlaceOrder command,
        RequestHandlerContinuation<Result<string>> handler)
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = fixture.ConnectionString);
        services.AddRedisIdempotency(p => p.ForRequests());
        services.AddSingleton<IRequestContext>(caller);
        services.AddSharedKernelApplication(typeof(RedisIdempotencyCallerScopingTests).Assembly, app => app.WithIdempotency());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        // The behavior is internal to SharedKernel.Application.Pipeline: resolve it the way the pipeline does.
        var behavior = scope.ServiceProvider.GetServices<IPipelineBehavior<PlaceOrder, Result<string>>>()
            .Single(b => b.GetType().Name.StartsWith("IdempotencyBehavior", StringComparison.Ordinal));

        using (RequestContextScope.Begin(caller))
            return await behavior.Handle(command, handler, CancellationToken.None);
    }

    private static async Task<List<string>> StoredKeysOf(string connectionString, TenantId tenantId)
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var server = multiplexer.GetServer(multiplexer.GetEndPoints()[0]);
        return server.Keys(pattern: $"sk:idempotency:{tenantId}:key:*").Select(key => key.ToString()).ToList();
    }

    [Fact]
    public async Task Behavior_TwoUsersOfOneTenant_SameKeyAndBody_EachGetsTheirOwnExecution_AndEachRetryReplaysItsOwn()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var alice = TestRequestContext.ForTenant(tenantId, "alice");
        var bob = TestRequestContext.ForTenant(tenantId, "bob");
        var command = new PlaceOrder($"order-{Guid.NewGuid():N}", "identical body");
        var executions = 0;

        RequestHandlerContinuation<Result<string>> HandlerFor(string owner) => () =>
        {
            executions++;
            return Task.FromResult(Result<string>.Success($"{owner}'s one-time secret"));
        };

        var aliceFirst = await SendAs(alice, command, HandlerFor("alice"));
        var bobFirst = await SendAs(bob, command, HandlerFor("bob"));
        var aliceRetry = await SendAs(alice, command, HandlerFor("alice (rerun)"));
        var bobRetry = await SendAs(bob, command, HandlerFor("bob (rerun)"));

        Assert.Equal(2, executions);
        Assert.Equal("alice's one-time secret", aliceFirst.Value);
        Assert.Equal("bob's one-time secret", bobFirst.Value);
        Assert.Equal("alice's one-time secret", aliceRetry.Value);
        Assert.Equal("bob's one-time secret", bobRetry.Value);

        var keys = await StoredKeysOf(fixture.ConnectionString, tenantId);
        Assert.Equal(2, keys.Distinct().Count());
        Assert.All(keys, key => Assert.Matches($"^sk:idempotency:{tenantId}:key:[0-9a-f]{{64}}$", key));
        Assert.DoesNotContain(keys, key => key.Contains(command.IdempotencyKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// The documented residual risk, against the real store: anonymous callers of one tenant share one scope, where
    /// only the fingerprint separates them.
    /// </summary>
    [Fact]
    public async Task Behavior_AnonymousCallersOfOneTenant_ReplayOnTheSameBody_AndConflictOnAnother()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var key = $"anonymous-{Guid.NewGuid():N}";
        var executions = 0;

        RequestHandlerContinuation<Result<string>> Handler(string response) => () =>
        {
            executions++;
            return Task.FromResult(Result<string>.Success(response));
        };

        await SendAs(TestRequestContext.Anonymous(tenantId), new PlaceOrder(key, "body"), Handler("first sender"));
        var sameBody = await SendAs(TestRequestContext.Anonymous(tenantId), new PlaceOrder(key, "body"), Handler("second sender"));
        var otherBody = await SendAs(TestRequestContext.Anonymous(tenantId), new PlaceOrder(key, "other body"), Handler("third sender"));

        Assert.Equal(1, executions);
        Assert.Equal("first sender", sameBody.Value);
        Assert.Equal("idempotency.key_reused", otherBody.Error.Code);
    }
}
