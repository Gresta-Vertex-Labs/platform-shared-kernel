using FluentAssertions;
using SharedKernel.Application.Behaviors.Caching.Tests.Support;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Caching;

/// <summary>
/// Covers the scope rules: per-query-type key namespacing, per-caller partitioning, and the
/// fail-closed behaviour when a declared scope has no identity on the request.
/// </summary>
public sealed class CachingBehaviorScopeTests
{
    private sealed record OrderQuery(string Id) : ICacheableQuery<OrderDto>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => Id;
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed record InvoiceQuery(string Id) : ICacheableQuery<InvoiceDto>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => Id;
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed record TenantScopedQuery : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => "k";
    }

    private sealed record UserScopedQuery : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => "k";
        public CacheScope Scope => CacheScope.User;
    }

    public sealed record OrderDto(string Reference, decimal Total);

    public sealed record InvoiceDto(string Number, decimal Amount);

    [Fact]
    public async Task Handle_TwoQueryTypesSharingAKeyString_DoNotShareAnEntry()
    {
        // Without a per-query-type namespace these two write the same key. The entry holds the bare
        // value as JSON, so the second query would deserialize the first payload into its own type
        // and return a partially-populated object rather than failing.
        var cache = new FakeCacheService();
        var orders = TestPipeline.Caching<OrderQuery, Result<OrderDto>>(cache);
        var invoices = TestPipeline.Caching<InvoiceQuery, Result<InvoiceDto>>(cache);

        await orders.Handle(new OrderQuery("42"), () => Task.FromResult(Result<OrderDto>.Success(new OrderDto("ORD-42", 10m))), CancellationToken.None);

        var invoiceHandlerRan = false;
        var invoice = await invoices.Handle(new InvoiceQuery("42"), () =>
        {
            invoiceHandlerRan = true;
            return Task.FromResult(Result<InvoiceDto>.Success(new InvoiceDto("INV-42", 99m)));
        }, CancellationToken.None);

        invoiceHandlerRan.Should().BeTrue("a different query type must not be served another query's entry");
        invoice.Value.Number.Should().Be("INV-42");
        cache.Keys.Should().BeEquivalentTo(
        [
            TestKeys.Global(nameof(OrderQuery), "42"),
            TestKeys.Global(nameof(InvoiceQuery), "42"),
        ]);
    }

    [Fact]
    public async Task Handle_TenantScopeWithNoResolvedTenant_SkipsTheCacheAndRunsTheHandler()
    {
        // Falling back to a global key here would let every request whose tenant resolution failed
        // share one entry, across tenants.
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<TenantScopedQuery, Result<string>>(cache, new FakeRequestContext(tenantId: null));
        var handlerRuns = 0;

        for (var i = 0; i < 2; i++)
        {
            var result = await behavior.Handle(new TenantScopedQuery(), () =>
            {
                handlerRuns++;
                return Task.FromResult(Result<string>.Success("v"));
            }, CancellationToken.None);

            result.Value.Should().Be("v");
        }

        handlerRuns.Should().Be(2, "nothing may be cached when the declared scope has no identity");
        cache.Keys.Should().BeEmpty();
        cache.ContextGetOrSetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoRequestContextRegisteredAtAll_TenantScopeStillFailsClosed()
    {
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<TenantScopedQuery, Result<string>>(cache);

        await behavior.Handle(new TenantScopedQuery(), () => Task.FromResult(Result<string>.Success("v")), CancellationToken.None);

        cache.Keys.Should().BeEmpty();
        cache.ContextGetOrSetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UserScope_PartitionsByCaller()
    {
        var cache = new FakeCacheService();
        var tenant = new TenantId(Guid.NewGuid());
        var alice = TestPipeline.Caching<UserScopedQuery, Result<string>>(cache, new FakeRequestContext(tenant, "alice"));
        var bob = TestPipeline.Caching<UserScopedQuery, Result<string>>(cache, new FakeRequestContext(tenant, "bob"));

        await alice.Handle(new UserScopedQuery(), () => Task.FromResult(Result<string>.Success("alice-data")), CancellationToken.None);

        var bobHandlerRan = false;
        var bobResult = await bob.Handle(new UserScopedQuery(), () =>
        {
            bobHandlerRan = true;
            return Task.FromResult(Result<string>.Success("bob-data"));
        }, CancellationToken.None);

        bobHandlerRan.Should().BeTrue("a user-scoped entry must never be served to a different caller");
        bobResult.Value.Should().Be("bob-data");
        cache.Keys.Should().BeEquivalentTo(
        [
            TestKeys.User(tenant, "alice", nameof(UserScopedQuery), "k"),
            TestKeys.User(tenant, "bob", nameof(UserScopedQuery), "k"),
        ]);
    }

    [Fact]
    public async Task Handle_UserScopeWithNoAuthenticatedCaller_SkipsTheCacheAndRunsTheHandler()
    {
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<UserScopedQuery, Result<string>>(
            cache, new FakeRequestContext(new TenantId(Guid.NewGuid()), userId: null));

        await behavior.Handle(new UserScopedQuery(), () => Task.FromResult(Result<string>.Success("v")), CancellationToken.None);

        cache.Keys.Should().BeEmpty();
        cache.ContextGetOrSetCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UserScopeWithoutATenant_StillPartitionsByCaller()
    {
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<UserScopedQuery, Result<string>>(
            cache, new FakeRequestContext(tenantId: null, userId: "alice"));

        await behavior.Handle(new UserScopedQuery(), () => Task.FromResult(Result<string>.Success("v")), CancellationToken.None);

        cache.Keys.Should().Equal(TestKeys.User(null, "alice", nameof(UserScopedQuery), "k"));
    }

    [Fact]
    public async Task Handle_SameTenantSameUser_ReadsItsOwnEntry()
    {
        var cache = new FakeCacheService();
        var tenant = new TenantId(Guid.NewGuid());
        var first = TestPipeline.Caching<UserScopedQuery, Result<string>>(cache, new FakeRequestContext(tenant, "alice"));
        var second = TestPipeline.Caching<UserScopedQuery, Result<string>>(cache, new FakeRequestContext(tenant, "alice"));

        await first.Handle(new UserScopedQuery(), () => Task.FromResult(Result<string>.Success("alice-data")), CancellationToken.None);

        var handlerRan = false;
        var result = await second.Handle(new UserScopedQuery(), () =>
        {
            handlerRan = true;
            return Task.FromResult(Result<string>.Success("unexpected"));
        }, CancellationToken.None);

        handlerRan.Should().BeFalse();
        result.Value.Should().Be("alice-data");
    }

    [Fact]
    public async Task Handle_KeyContainingTheSeparator_IsEscapedNotAmbiguous()
    {
        // "a:b" as an id must not be able to impersonate a different entity/id pair.
        var cache = new FakeCacheService();
        var behavior = TestPipeline.Caching<OrderQuery, Result<OrderDto>>(cache);

        await behavior.Handle(new OrderQuery("a:b"), () => Task.FromResult(Result<OrderDto>.Success(new OrderDto("r", 1m))), CancellationToken.None);

        cache.Keys.Should().Equal($"{FakeCacheKeyProvider.ServiceName}:{nameof(OrderQuery)}:a%3Ab");
    }
}
