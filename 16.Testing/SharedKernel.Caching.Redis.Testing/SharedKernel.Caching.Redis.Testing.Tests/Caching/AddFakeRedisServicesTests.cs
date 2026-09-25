using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Redis.HashStore;
using SharedKernel.Caching.Redis.PubSub;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

public sealed class AddFakeRedisServicesTests
{
    [Fact]
    public void AddFakeRedisServices_RegistersFakeRedisChannelService()
    {
        var provider = BuildProvider();
        Assert.IsType<FakeRedisChannelService>(provider.GetRequiredService<IRedisChannelService>());
    }

    [Fact]
    public void AddFakeRedisServices_RegistersFakeRedisHashService()
    {
        var provider = BuildProvider();
        Assert.IsType<FakeRedisHashService>(provider.GetRequiredService<IRedisHashService>());
    }

    [Fact]
    public void AddFakeRedisServices_RedisChannelAndHashServicesAreAlsoSingletons()
    {
        var provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<IRedisChannelService>(), provider.GetRequiredService<IRedisChannelService>());
        Assert.Same(provider.GetRequiredService<IRedisHashService>(), provider.GetRequiredService<IRedisHashService>());
    }

    [Fact]
    public async Task AddFakeRedisServices_RegisteredTimeProvider_DrivesHashExpiry()
    {
        var time = new FakeRedisHashServiceTests.ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(time);
        services.AddFakeRedisServices();
        using var provider = services.BuildServiceProvider();
        var hashes = provider.GetRequiredService<IRedisHashService>();

        await hashes.SetFieldAsync("session", "a", "1", FakeRedisHashServiceTestJsonContext.Default.String, TimeSpan.FromMinutes(1));
        time.Advance(TimeSpan.FromMinutes(1));

        Assert.False((await hashes.GetFieldAsync("session", "a", FakeRedisHashServiceTestJsonContext.Default.String)).IsHit);
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddFakeRedisServices();
        return services.BuildServiceProvider();
    }
}
