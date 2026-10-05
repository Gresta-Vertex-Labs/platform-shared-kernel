using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Idempotency;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.Extensions;
using SharedKernel.Persistence;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Execution;
using SharedKernel.Testing.Persistence;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Concurrency;

/// <summary>
/// <c>05.Application</c>'s <c>IdempotencyBehavior</c> over the real PostgreSQL store, registered the way a service
/// registers it: reservations are scoped per tenant and caller (P-562 X3, merged in P-579), and the key the store
/// receives is a fixed-length digest that fits the <c>key</c> column however long the command's own key is.
/// </summary>
/// <remarks>REQUIRES A DOCKER DAEMON (Testcontainers PostgreSQL).</remarks>
[Collection("PostgreSqlContainer")]
public sealed class EfCoreIdempotencyCallerScopingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;

    public EfCoreIdempotencyCallerScopingTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record PlaceOrder(string IdempotencyKey, string Body) : ICommand<string>, IIdempotentRequest;

    private IdempotencyDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdempotencyDbContext>();
        optionsBuilder.UsePostgres(TestNpgsqlDataSources.Get(_fixture.ConnectionString));
        return new IdempotencyDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// Sends <paramref name="command"/> through the idempotency behavior as <paramref name="caller"/>, each call on its own
    /// container and context. The caller is both the request context the behavior scopes the key by and the ambient
    /// context the store takes its tenant from.
    /// </summary>
    private async Task<Result<string>> SendAs(
        TestRequestContext caller,
        PlaceOrder command,
        RequestHandlerContinuation<Result<string>> handler)
    {
        var services = new ServiceCollection();
        services.AddClock();
        services.AddEfCoreIdempotency(
            options => options.UsePostgres(TestNpgsqlDataSources.Get(_fixture.ConnectionString)),
            p => p.ForRequests());
        services.AddSingleton<IRequestContext>(caller);
        services.AddSharedKernelApplication(typeof(EfCoreIdempotencyCallerScopingTests).Assembly, app => app.WithIdempotency());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        // The behavior is internal to SharedKernel.Application.Pipeline: resolve it the way the pipeline does.
        var behavior = scope.ServiceProvider.GetServices<IPipelineBehavior<PlaceOrder, Result<string>>>()
            .Single(b => b.GetType().Name.StartsWith("IdempotencyBehavior", StringComparison.Ordinal));

        using (RequestContextScope.Begin(caller))
            return await behavior.Handle(command, handler, CancellationToken.None);
    }

    private async Task<List<string>> StoredKeysOf(TenantId tenantId)
    {
        var scope = IdempotencyTenantScope.For(tenantId);
        await using var context = CreateContext();
        return await context.IdempotencyKeys.AsNoTracking()
            .Where(row => row.TenantScope == scope && row.Purpose == IdempotencyPurpose.Request)
            .Select(row => row.Key)
            .ToListAsync();
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

        var keys = await StoredKeysOf(tenantId);
        Assert.Equal(2, keys.Distinct().Count());
        Assert.All(keys, key => Assert.Matches("^[0-9a-f]{64}$", key));
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

    /// <summary>
    /// The <c>key</c> column holds 512 characters. A command's own key longer than that used to fail the insert;
    /// the store now receives the 64-character digest, so any key length reserves and replays.
    /// </summary>
    [Fact]
    public async Task Behavior_KeyLongerThanTheKeyColumn_ReservesAndReplays()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var caller = TestRequestContext.ForTenant(tenantId, "alice");
        var command = new PlaceOrder(new string('k', 2_000), "body");
        var executions = 0;

        RequestHandlerContinuation<Result<string>> handler = () =>
        {
            executions++;
            return Task.FromResult(Result<string>.Success("done"));
        };

        var first = await SendAs(caller, command, handler);
        var retry = await SendAs(caller, command, handler);

        Assert.Equal(1, executions);
        Assert.Equal("done", first.Value);
        Assert.Equal("done", retry.Value);
        Assert.Equal(64, Assert.Single(await StoredKeysOf(tenantId)).Length);
    }
}
