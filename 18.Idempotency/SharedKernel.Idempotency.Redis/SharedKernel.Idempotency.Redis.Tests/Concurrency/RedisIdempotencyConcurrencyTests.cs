using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Idempotency.Redis.KeyStore;
using SharedKernel.Idempotency.Redis.MessageStore;
using SharedKernel.Idempotency.Redis.Options;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.Abstractions.TenantContext;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Concurrency;

/// <summary>
/// Concurrency-proving tests against a real Redis container (Testcontainers). Never mock the
/// backing store here — a mock cannot exhibit the race these tests exist to rule out.
/// </summary>
/// <remarks>
/// REQUIRES A DOCKER DAEMON. Written to satisfy 18.Idempotency/state-map.md's T-01 through T-04
/// (T-05, fail-open/fail-closed, lives in <see cref="RedisIdempotencyFailOpenTests"/>). In an
/// environment with no reachable Docker daemon, these tests fail at collection fixture setup
/// (<see cref="RedisContainerFixture.InitializeAsync"/>), not inside the test bodies themselves.
/// </remarks>
[Collection("RedisContainer")]
public sealed class RedisIdempotencyConcurrencyTests(RedisContainerFixture fixture)
{
    private sealed class FixedTenantAccessor(Guid? tenantId) : ITenantContextAccessor
    {
        public Guid? TenantId { get; } = tenantId;
    }

    private static IConnectionMultiplexer CreateMultiplexer(string connectionString) =>
        ConnectionMultiplexer.Connect(connectionString);

    private static RedisIdempotencyKeyStore CreateKeyStore(
        IConnectionMultiplexer multiplexer,
        Guid? tenantId,
        RedisIdempotencyOptions? options = null) =>
        new(
            multiplexer,
            new FixedTenantAccessor(tenantId),
            MsOptions.Create(options ?? new RedisIdempotencyOptions { InFlightTtl = TimeSpan.FromMilliseconds(300) }),
            new InMemoryLogger<RedisIdempotencyKeyStore>());

    private static RedisIdempotencyMessageStore CreateMessageStore(
        IConnectionMultiplexer multiplexer,
        Guid? tenantId,
        RedisIdempotencyOptions? redisOptions = null) =>
        new(
            multiplexer,
            new FixedTenantAccessor(tenantId),
            MsOptions.Create(redisOptions ?? new RedisIdempotencyOptions { InFlightTtl = TimeSpan.FromMilliseconds(300) }),
            MsOptions.Create(new IdempotencyOptions { ExpiryWindow = TimeSpan.FromHours(1) }),
            new InMemoryLogger<RedisIdempotencyMessageStore>());

    // T-01: N genuinely concurrent HasProcessedAsync calls with the identical key — exactly one
    // must observe "not yet processed" (false).
    [Fact]
    public async Task HasProcessedAsync_ConcurrentCallsWithSameKey_ExactlyOneWinsTheReservation()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var store = CreateKeyStore(multiplexer, tenantId);
        var idempotencyKey = $"concurrent-{Guid.NewGuid():N}";

        const int concurrency = 32;
        using var barrier = new Barrier(concurrency);

        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await store.HasProcessedAsync(idempotencyKey, CancellationToken.None);
        }));

        var results = await Task.WhenAll(tasks);

        Assert.Single(results, hasProcessed => hasProcessed == false);
        Assert.Equal(concurrency - 1, results.Count(hasProcessed => hasProcessed));
    }

    // T-02: an unconfirmed reservation self-heals once its short InFlightTtl elapses.
    [Fact]
    public async Task HasProcessedAsync_AfterInFlightTtlElapsesWithoutConfirmation_BecomesRetryable()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var options = new RedisIdempotencyOptions { InFlightTtl = TimeSpan.FromMilliseconds(200) };
        var store = CreateKeyStore(multiplexer, tenantId, options);
        var idempotencyKey = $"self-heal-{Guid.NewGuid():N}";

        var firstAttempt = await store.HasProcessedAsync(idempotencyKey, CancellationToken.None);
        Assert.False(firstAttempt); // fresh reservation

        // Deliberately never call MarkProcessedAsync — simulating a fault (Domain Invariant 2).
        await Task.Delay(options.InFlightTtl + TimeSpan.FromMilliseconds(300));

        var secondAttempt = await store.HasProcessedAsync(idempotencyKey, CancellationToken.None);
        Assert.False(secondAttempt); // retryable again — the key was never permanently consumed
    }

    // T-03: PEXPIRE (confirm) and the Lua response write must never clobber each other, in either
    // order. Deliberately uses an explicit, generous InFlightTtl rather than CreateKeyStore's own
    // 300ms fallback (that value exists only to make T-02's self-heal-on-expiry test fast — it has
    // nothing to do with what T-03 verifies). This test performs 3-4 sequential real network round
    // trips to the Redis container before its final assertion; on a slow/contended CI runner those
    // round trips can cumulatively exceed a sub-second TTL, which would silently expire the whole
    // key (sentinel + response together) between the reserve and the confirm and make the response
    // vanish with no error — a genuine defect class, but of test sizing, not of the store's atomicity
    // protocol (verified by deterministic reproduction with an injected delay during diagnosis).
    // Covers both directions explicitly: the theory data's "confirm-first" branch also matches the
    // actual, only-sanctioned production call order documented on IIdempotencyResponseStore
    // ("HasProcessedAsync/MarkProcessedAsync are always called first, regardless of replay support").
    [Theory]
    [InlineData(true)] // storeResponseFirst — defensive: the store's own commutativity, not the documented calling contract
    [InlineData(false)] // confirmFirst — the actual, documented production order
    public async Task ConfirmThenStoreResponse_InEitherOrder_PreservesTheStoredResponse(bool storeResponseFirst)
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantId = Guid.NewGuid();
        var store = CreateKeyStore(multiplexer, tenantId, new RedisIdempotencyOptions());
        var idempotencyKey = $"order-independence-{storeResponseFirst}-{Guid.NewGuid():N}";
        const string payload = """{"orderId":42,"status":"accepted"}""";

        await store.HasProcessedAsync(idempotencyKey, CancellationToken.None);

        if (storeResponseFirst)
        {
            await store.StoreResponseAsync(idempotencyKey, payload, CancellationToken.None);
            await store.MarkProcessedAsync(idempotencyKey, CancellationToken.None);
        }
        else
        {
            await store.MarkProcessedAsync(idempotencyKey, CancellationToken.None);
            await store.StoreResponseAsync(idempotencyKey, payload, CancellationToken.None);
        }

        var stored = await store.TryGetStoredResponseAsync(idempotencyKey, CancellationToken.None);
        Assert.Equal(payload, stored);
    }

    // T-04: two tenants reserving the identical raw key concurrently must not collide.
    [Fact]
    public async Task HasProcessedAsync_TwoTenantsWithIdenticalRawKey_BothReservationsSucceedIndependently()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var storeA = CreateKeyStore(multiplexer, tenantA);
        var storeB = CreateKeyStore(multiplexer, tenantB);
        var sharedRawKey = $"shared-{Guid.NewGuid():N}";

        var resultsA = await storeA.HasProcessedAsync(sharedRawKey, CancellationToken.None);
        var resultsB = await storeB.HasProcessedAsync(sharedRawKey, CancellationToken.None);

        Assert.False(resultsA);
        Assert.False(resultsB);
    }

    // T-04-equivalent for the message store, mirroring the key-store proof for IIdempotencyStore.
    [Fact]
    public async Task MessageStore_TwoTenantsWithIdenticalMessageId_BothReservationsSucceedIndependently()
    {
        await using var multiplexer = CreateMultiplexer(fixture.ConnectionString);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var storeA = CreateMessageStore(multiplexer, tenantA);
        var storeB = CreateMessageStore(multiplexer, tenantB);
        var sharedMessageId = Guid.NewGuid();

        var hasProcessedA = await storeA.HasProcessedAsync(sharedMessageId, CancellationToken.None);
        var hasProcessedB = await storeB.HasProcessedAsync(sharedMessageId, CancellationToken.None);

        Assert.False(hasProcessedA);
        Assert.False(hasProcessedB);
    }
}
