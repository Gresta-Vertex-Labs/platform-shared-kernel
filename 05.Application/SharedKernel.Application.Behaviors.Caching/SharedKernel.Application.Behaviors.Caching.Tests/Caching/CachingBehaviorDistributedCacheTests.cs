using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using ZiggyCreatures.Caching.Fusion;

namespace SharedKernel.Application.Behaviors.Caching.Tests.Caching;

/// <summary>
/// Runs <see cref="CachingBehavior{TRequest,TResponse}"/> against two FusionCache instances sharing one
/// distributed cache, so a hit on the second instance has been serialized and read back.
/// </summary>
public sealed class CachingBehaviorDistributedCacheTests
{
    public sealed record WidgetDto(string Id, int Count, IReadOnlyList<string> Labels);

    private sealed record WidgetQuery(string Id) : ICacheableQuery<WidgetDto>
    {
        public CachePolicy CachePolicy => CachePolicy.Default;
        public string CacheKey => $"widget:{Id}";
    }

    [Fact]
    public async Task Handle_ValueCachedByOneInstance_IsReadBackFromTheDistributedCacheByAnother()
    {
        var distributed = NewDistributedCache();
        await using var writer = BuildProvider(distributed);
        await using var reader = BuildProvider(distributed);
        var widget = new WidgetDto("1", 3, ["red", "large"]);

        var written = await Behavior(writer).Handle(
            new WidgetQuery("1"), () => Task.FromResult(Result<WidgetDto>.Success(widget)), CancellationToken.None);

        var read = await Behavior(reader).Handle(
            new WidgetQuery("1"),
            () => throw new InvalidOperationException("The handler must not run on a distributed cache hit."),
            CancellationToken.None);

        written.Value.Should().Be(widget);
        read.IsSuccess.Should().BeTrue();
        read.Value.Should().BeEquivalentTo(widget);
    }

    [Fact]
    public async Task Handle_Failure_IsReturnedAndNotWrittenToTheDistributedCache()
    {
        var distributed = NewDistributedCache();
        await using var writer = BuildProvider(distributed);
        await using var reader = BuildProvider(distributed);

        var failed = await Behavior(writer).Handle(
            new WidgetQuery("2"),
            () => Task.FromResult(Result<WidgetDto>.Failure(Error.NotFound("widget.not_found", "missing"))),
            CancellationToken.None);

        var handlerRan = false;
        var recovered = await Behavior(reader).Handle(
            new WidgetQuery("2"),
            () =>
            {
                handlerRan = true;
                return Task.FromResult(Result<WidgetDto>.Success(new WidgetDto("2", 1, [])));
            },
            CancellationToken.None);

        failed.IsFailure.Should().BeTrue();
        failed.Error.Code.Should().Be("widget.not_found");
        handlerRan.Should().BeTrue();
        recovered.Value.Id.Should().Be("2");
    }

    private static MemoryDistributedCache NewDistributedCache() =>
        new(Options.Create(new MemoryDistributedCacheOptions()));

    private static CachingBehavior<WidgetQuery, Result<WidgetDto>> Behavior(IServiceProvider provider) =>
        new(provider.GetRequiredService<ICacheService>());

    private static ServiceProvider BuildProvider(IDistributedCache distributed)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(distributed);
        services.AddSharedKernelCaching(o => o.ServiceName = "behaviors-caching-tests");
        services.AddFusionCache().WithRegisteredDistributedCache(ignoreMemoryDistributedCache: false);
        return services.BuildServiceProvider();
    }
}
