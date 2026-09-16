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
    private sealed record TestQuery : IQuery<string>, ICacheableQuery<Result<string>>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => "key";
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
    public async Task Build_WithICacheServiceRegistered_ResolvesAndCachesThroughRealDispatch()
    {
        var cache = new FakeCacheService();
        var services = new ServiceCollection();
        services.AddSingleton<ICacheService>(cache);
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CachingBehaviorsExtensionsTests>());

        services.AddSharedKernelApplicationBehaviors().AddCachingBehaviors().Build();

        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var first = await sender.Send(new TestQuery());
        var second = await sender.Send(new TestQuery());

        first.Value.Should().Be("value");
        second.Value.Should().Be("value");
        cache.SetCalls.Should().ContainSingle("the second dispatch must be served from cache, never re-invoking the handler");
    }
}
