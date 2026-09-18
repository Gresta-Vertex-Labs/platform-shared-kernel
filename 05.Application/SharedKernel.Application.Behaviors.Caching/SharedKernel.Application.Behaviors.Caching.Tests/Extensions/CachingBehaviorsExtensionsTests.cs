using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Behaviors.Caching.Extensions;
using SharedKernel.Application.Behaviors.Caching.Tests.Support;
using SharedKernel.Application.Behaviors.Extensions;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Extensions;

public sealed class CachingBehaviorsExtensionsTests
{
    private sealed record TestQuery : ICacheableQuery<string>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => "key";
        public CacheScope Scope => CacheScope.Global;
    }

    private sealed class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        public Task<Result<string>> Handle(TestQuery request, CancellationToken cancellationToken)
            => Task.FromResult(Result<string>.Success("value"));
    }

    [Fact]
    public void Build_WithoutICacheServiceRegistered_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelApplicationBehaviors().AddCachingBehaviors();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ICacheService*");
    }

    [Fact]
    public void Build_WithoutITenantCacheKeyProviderRegistered_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICacheService>(new FakeCacheService());
        var builder = services.AddSharedKernelApplicationBehaviors().AddCachingBehaviors();

        var act = () => builder.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*ITenantCacheKeyProvider*");
    }

    [Fact]
    public void AddCachingBehaviors_RegistersTheMetricsDependencyItsBehaviorsResolve()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICacheService>(new FakeCacheService());
        services.AddSingleton<ITenantCacheKeyProvider>(new FakeCacheKeyProvider());

        services.AddSharedKernelApplicationBehaviors().AddCachingBehaviors().Build();

        using var provider = services.BuildServiceProvider();

        // The enumerable form is the one MediatR resolves, and it skips open generics whose
        // constraints the request type does not satisfy -- so a query yields the caching behavior
        // and never the command-side invalidation behavior.
        var behaviors = provider.GetServices<IPipelineBehavior<TestQuery, Result<string>>>().ToList();

        behaviors.Should().ContainSingle("every dependency the registered behavior declares must be resolvable")
            .Which.Should().BeOfType<CachingBehavior<TestQuery, Result<string>>>();
    }

    [Fact]
    public async Task Build_WithDependenciesRegistered_ResolvesAndCachesThroughRealDispatch()
    {
        var cache = new FakeCacheService();
        var services = new ServiceCollection();
        services.AddSingleton<ICacheService>(cache);
        services.AddSingleton<ITenantCacheKeyProvider>(new FakeCacheKeyProvider());
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CachingBehaviorsExtensionsTests>());

        services.AddSharedKernelApplicationBehaviors().AddCachingBehaviors().Build();

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var first = await sender.Send(new TestQuery());
        var second = await sender.Send(new TestQuery());

        first.Value.Should().Be("value");
        second.Value.Should().Be("value");
        cache.SetCalls.Should().ContainSingle("the second dispatch must be served from cache, never re-invoking the handler");
        cache.Keys.Should().Equal(TestKeys.Global(nameof(TestQuery), "key"));
    }
}
