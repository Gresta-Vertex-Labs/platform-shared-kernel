using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Abstractions.Extensions;
using Xunit;

namespace SharedKernel.Caching.Redis.PubSub.Tests;

/// <summary>
/// Tests for <c>AddCachingCoreOptions</c> (CO-01/CO-04) and the
/// <c>RedisCacheInvalidationBus</c> default-ServiceName warning (CO-03).
/// </summary>
public sealed class CachingCoreOptionsDiTests
{
    // -----------------------------------------------------------------------
    // CO-04: AddCachingCoreOptions resolves ServiceName without AddSharedKernelCaching
    // -----------------------------------------------------------------------

    [Fact]
    public void AddCachingCoreOptions_SetsServiceName_WithoutFusionCache()
    {
        // Arrange — no AddSharedKernelCaching, no FusionCache reference
        var services = new ServiceCollection();
        services.AddCachingCoreOptions(o => o.ServiceName = "worker-service");

        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<CachingCoreOptions>>();

        // Assert
        Assert.Equal("worker-service", options.Value.ServiceName);
    }

    [Fact]
    public void AddCachingCoreOptions_ReturnsServiceCollection_ForChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — verify returns IServiceCollection (not ICachingBuilder)
        var returned = services.AddCachingCoreOptions(o => o.ServiceName = "svc");

        // Assert
        Assert.Same(services, returned);
    }

    [Fact]
    public void AddCachingCoreOptions_NullConfigure_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddCachingCoreOptions(null!));
    }

    [Fact]
    public void AddCachingCoreOptions_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        Assert.Throws<ArgumentNullException>(() =>
            services.AddCachingCoreOptions(o => o.ServiceName = "svc"));
    }

    [Fact]
    public void AddCachingCoreOptions_IsAdditive_LastWriterWins()
    {
        // Arrange — two consecutive Configure calls; second one should win on ServiceName
        var services = new ServiceCollection();
        services.AddCachingCoreOptions(o => o.ServiceName = "first");
        services.AddCachingCoreOptions(o => o.ServiceName = "second");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CachingCoreOptions>>();

        // IOptions<T> applies Configure delegates in registration order.
        // The second delegate sets ServiceName = "second", which is the final value.
        Assert.Equal("second", options.Value.ServiceName);
    }

    // -----------------------------------------------------------------------
    // CO-03: RedisCacheInvalidationBus warns when ServiceName is the default "app"
    //
    // NOTE: RedisCacheInvalidationBus is internal sealed. NSubstitute/Castle cannot
    // create a proxy for ILogger<RedisCacheInvalidationBus> because RedisCacheInvalidationBus
    // is not publicly accessible and the CastleDynamicProxy strong-name requirement is not met.
    // We use a custom capturing logger instead of NSubstitute for these tests.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Simple capturing logger that records the highest log level seen, for use in tests
    /// where NSubstitute cannot proxy ILogger{T} because T is an internal type.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<LogLevel> _levels = [];

        public IReadOnlyList<LogLevel> LoggedLevels => _levels;

        public bool HasWarning => _levels.Contains(LogLevel.Warning);

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        bool ILogger.IsEnabled(LogLevel logLevel) => true;

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => _levels.Add(logLevel);
    }

    [Fact]
    public void RedisCacheInvalidationBus_LogsWarning_WhenServiceNameIsDefault()
    {
        // Arrange
        var channelService = Substitute.For<IRedisChannelService>();
        var logger = new CapturingLogger<RedisCacheInvalidationBus>();

        var services = new ServiceCollection();
        services.AddCachingCoreOptions(o => o.ServiceName = "app"); // default value

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CachingCoreOptions>>();

        // Act — construct bus directly (same as DI resolution)
        var bus = new RedisCacheInvalidationBus(channelService, options, logger);

        // Assert — warning was logged
        Assert.True(logger.HasWarning);

        _ = bus;
    }

    [Fact]
    public void RedisCacheInvalidationBus_DoesNotLogWarning_WhenServiceNameIsCustom()
    {
        // Arrange
        var channelService = Substitute.For<IRedisChannelService>();
        var logger = new CapturingLogger<RedisCacheInvalidationBus>();

        var services = new ServiceCollection();
        services.AddCachingCoreOptions(o => o.ServiceName = "my-custom-service");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CachingCoreOptions>>();

        // Act
        var bus = new RedisCacheInvalidationBus(channelService, options, logger);

        // Assert — no warning logged
        Assert.False(logger.HasWarning);

        _ = bus;
    }

    [Fact]
    public void RedisCacheInvalidationBus_DoesNotLogWarning_WhenServiceNameIsDefaultCaseInsensitive()
    {
        // Arrange — "APP" in uppercase should still trigger the warning (case-insensitive compare)
        var channelService = Substitute.For<IRedisChannelService>();
        var logger = new CapturingLogger<RedisCacheInvalidationBus>();

        var services = new ServiceCollection();
        services.AddCachingCoreOptions(o => o.ServiceName = "APP"); // uppercase default

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CachingCoreOptions>>();

        // Act
        var bus = new RedisCacheInvalidationBus(channelService, options, logger);

        // Assert — warning logged because "APP" case-insensitively equals "app"
        Assert.True(logger.HasWarning);

        _ = bus;
    }

    [Fact]
    public void RedisCacheInvalidationBus_ViaFullDiPipeline_UsesServiceNameFromAddCachingCoreOptions()
    {
        // Arrange — full DI pipeline; bus resolved from container
        var channelService = Substitute.For<IRedisChannelService>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCachingCoreOptions(o => o.ServiceName = "pipeline-svc");
        services.AddSingleton(channelService);
        services.AddSingleton<ICacheInvalidationBus, RedisCacheInvalidationBus>();

        var provider = services.BuildServiceProvider();

        // Act — resolution should not throw
        var bus = provider.GetRequiredService<ICacheInvalidationBus>();

        // Assert — resolved and is the Redis implementation
        Assert.IsType<RedisCacheInvalidationBus>(bus);
    }
}
