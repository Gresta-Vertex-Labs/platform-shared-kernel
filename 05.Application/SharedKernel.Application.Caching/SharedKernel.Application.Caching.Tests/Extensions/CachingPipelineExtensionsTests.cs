using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Caching.Tests.Support;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Caching.Tests.Extensions;

public sealed class CachingPipelineExtensionsTests
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

    private static ServiceCollection WithCaching()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(typeof(CachingPipelineExtensionsTests).Assembly, app => app.WithCaching());
        return services;
    }

    private static void ValidateOnStart(IServiceProvider provider)
        => provider.GetRequiredService<IStartupValidator>().Validate();

    [Fact]
    public void Start_WithoutICacheServiceOrKeyProvider_FailsNamingBoth()
    {
        using var provider = WithCaching().BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*ICacheService*ITenantCacheKeyProvider*");
    }

    [Fact]
    public void Start_WithoutITenantCacheKeyProvider_Fails()
    {
        var services = WithCaching();
        services.AddSingleton<ICacheService>(new FakeCacheService());
        using var provider = services.BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        act.Should().Throw<OptionsValidationException>().WithMessage("*ITenantCacheKeyProvider*CachingBehavior<,>*");
    }

    [Fact]
    public void Start_WithDependenciesRegisteredAfterTheCall_Succeeds()
    {
        var services = WithCaching();
        services.AddSingleton<ICacheService>(new FakeCacheService());
        services.AddSingleton<ITenantCacheKeyProvider>(new FakeCacheKeyProvider());
        using var provider = services.BuildServiceProvider();

        var act = () => ValidateOnStart(provider);

        act.Should().NotThrow();
    }

    [Fact]
    public void WithCaching_RegistersTheMetricsDependencyItsBehaviorsResolve()
    {
        var services = WithCaching();
        services.AddSingleton<ICacheService>(new FakeCacheService());
        services.AddSingleton<ITenantCacheKeyProvider>(new FakeCacheKeyProvider());

        using var provider = services.BuildServiceProvider();

        // The enumerable form is the one MediatR resolves, and it skips open generics whose
        // constraints the request type does not satisfy -- so a query yields the caching behavior
        // and never the command-side invalidation behavior.
        var behaviors = provider.GetServices<IPipelineBehavior<TestQuery, Result<string>>>().ToList();

        behaviors.OfType<CachingBehavior<TestQuery, Result<string>>>()
            .Should().ContainSingle("every dependency the registered behavior declares must be resolvable");
    }

    [Fact]
    public void WithCaching_CalledTwice_RegistersEachBehaviorOnce()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelApplication(
            typeof(CachingPipelineExtensionsTests).Assembly,
            app => app.WithCaching().WithCaching());
        services.AddSingleton<ICacheService>(new FakeCacheService());
        services.AddSingleton<ITenantCacheKeyProvider>(new FakeCacheKeyProvider());
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IPipelineBehavior<TestQuery, Result<string>>>()
            .OfType<CachingBehavior<TestQuery, Result<string>>>()
            .Should().ContainSingle();
    }

    [Fact]
    public async Task WithCaching_WithDependenciesRegistered_ResolvesAndCachesThroughRealDispatch()
    {
        var cache = new FakeCacheService();
        var services = WithCaching();
        services.AddSingleton<ICacheService>(cache);
        services.AddSingleton<ITenantCacheKeyProvider>(new FakeCacheKeyProvider());

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
