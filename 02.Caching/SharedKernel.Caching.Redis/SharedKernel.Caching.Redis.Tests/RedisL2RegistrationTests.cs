using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Caching.Redis.Extensions;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace SharedKernel.Caching.Redis.Tests;

/// <summary>
/// Both <c>AddRedisL2</c> overloads without a running Redis: prerequisites, duplicate registration,
/// <see cref="RedisL2Options"/> binding and validation, and the breaker durations reaching
/// <see cref="FusionCacheOptions"/>.
/// </summary>
public sealed class RedisL2RegistrationTests
{
    // ─── Prerequisites and duplicates ─────────────────────────────────────────────

    [Fact]
    public void DelegateOverload_WithoutAddRedisConnection_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "svc");

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddRedisL2());

        Assert.Contains("AddRedisConnection", exception.Message);
        Assert.Contains("AddRedisL2", exception.Message);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDistributedCache));
    }

    [Fact]
    public void ConfigurationOverload_WithoutAddRedisConnection_Throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "svc");

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddRedisL2(Configuration()));

        Assert.Contains("AddRedisConnection", exception.Message);
    }

    [Fact]
    public void AddRedisL2_CalledTwice_Throws()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "svc").AddRedisL2();

        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddRedisL2());
        Assert.Contains("already been called", exception.Message);

        Assert.Throws<InvalidOperationException>(() => builder.AddRedisL2(Configuration()));
    }

    [Fact]
    public void ConfigurationOverloadThenDelegateOverload_Throws()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "svc").AddRedisL2(Configuration());

        Assert.Throws<InvalidOperationException>(() => builder.AddRedisL2(o => o.KeyPrefix = "x"));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        ICachingBuilder nullBuilder = null!;
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "svc");

        Assert.Throws<ArgumentNullException>(() => nullBuilder.AddRedisL2());
        Assert.Throws<ArgumentNullException>(() => nullBuilder.AddRedisL2(Configuration()));
        Assert.Throws<ArgumentNullException>(() => builder.AddRedisL2((IConfiguration)null!));
    }

    [Fact]
    public void AddRedisL2_ReturnsTheSameBuilder_AndRegistersNoGlobalDistributedCache()
    {
        var services = new ServiceCollection();
        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "svc");

        Assert.Same(builder, builder.AddRedisL2());

        // The distributed layer is handed to FusionCache only; it runs on the shared connection and nothing
        // resolved from DI may use or dispose it.
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDistributedCache));    }

    // ─── Backplane channel prefix ─────────────────────────────────────────────────

    [Fact]
    public void KeyPrefix_BecomesTheBackplaneChannelPrefix()
    {
        using var provider = BuildDelegateProvider(o => o.KeyPrefix = "staging:");

        Assert.Equal("staging:", provider.GetRequiredService<IOptions<FusionCacheOptions>>().Value.BackplaneChannelPrefix);
    }

    [Fact]
    public void BoundKeyPrefix_BecomesTheBackplaneChannelPrefix_AfterTheDelegate()
    {
        using var provider = BuildConfigurationProvider(
            new Dictionary<string, string?> { ["SharedKernel:Caching:Redis:L2:KeyPrefix"] = "from-config:" },
            o => o.KeyPrefix = "from-delegate:");

        Assert.Equal("from-delegate:", provider.GetRequiredService<IOptions<FusionCacheOptions>>().Value.BackplaneChannelPrefix);
    }

    [Fact]
    public void EmptyKeyPrefix_LeavesTheBackplaneChannelPrefixUnset()
    {
        using var provider = BuildDelegateProvider(_ => { });

        Assert.Null(provider.GetRequiredService<IOptions<FusionCacheOptions>>().Value.BackplaneChannelPrefix);
    }

    [Fact]
    public void EmptyKeyPrefix_KeepsABackplaneChannelPrefixSetByTheApplication()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");
        services.Configure<FusionCacheOptions>(o => o.BackplaneChannelPrefix = "app-chosen");
        services.AddSharedKernelCaching(o => o.ServiceName = "svc").AddRedisL2();
        using var provider = services.BuildServiceProvider();

        Assert.Equal("app-chosen", provider.GetRequiredService<IOptions<FusionCacheOptions>>().Value.BackplaneChannelPrefix);
    }

    // ─── Defaults and binding ─────────────────────────────────────────────────────

    [Fact]
    public void SectionName_IsSharedKernelCachingRedisL2() =>
        Assert.Equal("SharedKernel:Caching:Redis:L2", RedisL2Options.SectionName);

    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new RedisL2Options();

        Assert.Equal(string.Empty, options.KeyPrefix);
        Assert.Equal(TimeSpan.FromSeconds(2), options.DistributedCacheCircuitBreakerDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), options.BackplaneCircuitBreakerDuration);
    }

    [Fact]
    public void ConfigurationOverload_BindsEverySetting()
    {
        using var provider = BuildConfigurationProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:Redis:L2:KeyPrefix"] = "staging:",
            ["SharedKernel:Caching:Redis:L2:DistributedCacheCircuitBreakerDuration"] = "00:00:07",
            ["SharedKernel:Caching:Redis:L2:BackplaneCircuitBreakerDuration"] = "00:00:00.500",
        });

        var options = provider.GetRequiredService<IOptions<RedisL2Options>>().Value;

        Assert.Equal("staging:", options.KeyPrefix);
        Assert.Equal(TimeSpan.FromSeconds(7), options.DistributedCacheCircuitBreakerDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.BackplaneCircuitBreakerDuration);
    }

    [Fact]
    public void ConfigurationOverload_MissingSection_KeepsDefaultsAndPassesValidation()
    {
        using var provider = BuildConfigurationProvider(new Dictionary<string, string?>());

        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptions<RedisL2Options>>().Value;

        Assert.Equal(string.Empty, options.KeyPrefix);
        Assert.Equal(TimeSpan.FromSeconds(2), options.DistributedCacheCircuitBreakerDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), options.BackplaneCircuitBreakerDuration);
    }

    [Fact]
    public void ConfigurationOverload_ConnectionSectionKeys_AreNotBoundIntoL2()
    {
        using var provider = BuildConfigurationProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:Redis:KeyPrefix"] = "parent-section:",
            ["SharedKernel:Caching:Redis:L2:KeyPrefix"] = "l2-section:",
        });

        Assert.Equal("l2-section:", provider.GetRequiredService<IOptions<RedisL2Options>>().Value.KeyPrefix);
    }

    [Fact]
    public void ConfigurationOverload_DelegateRunsAfterBinding()
    {
        using var provider = BuildConfigurationProvider(
            new Dictionary<string, string?>
            {
                ["SharedKernel:Caching:Redis:L2:KeyPrefix"] = "from-config:",
                ["SharedKernel:Caching:Redis:L2:BackplaneCircuitBreakerDuration"] = "00:00:05",
            },
            o =>
            {
                Assert.Equal("from-config:", o.KeyPrefix);
                o.KeyPrefix = "from-delegate:";
            });

        var options = provider.GetRequiredService<IOptions<RedisL2Options>>().Value;

        Assert.Equal("from-delegate:", options.KeyPrefix);
        Assert.Equal(TimeSpan.FromSeconds(5), options.BackplaneCircuitBreakerDuration);
    }

    // ─── Validation ───────────────────────────────────────────────────────────────

    public static TheoryData<TimeSpan> OutOfRangeDurations => new()
    {
        TimeSpan.FromTicks(-1),
        TimeSpan.FromSeconds(-5),
        TimeSpan.FromMinutes(10) + TimeSpan.FromTicks(1),
        TimeSpan.FromHours(1),
    };

    public static TheoryData<TimeSpan> InRangeDurations => new()
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromMinutes(10),
    };

    [Fact]
    public void KeyPrefixLongerThan64Characters_FailsAtStartupAndOnOptionsAccess()
    {
        using var provider = BuildDelegateProvider(o => o.KeyPrefix = new string('k', 65));

        var startup = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("KeyPrefix must be at most 64 characters.", startup.Failures);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RedisL2Options>>().Value);
    }

    [Fact]
    public void KeyPrefixOf64Characters_PassesValidation()
    {
        using var provider = BuildDelegateProvider(o => o.KeyPrefix = new string('k', 64));

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void NullKeyPrefix_Fails()
    {
        using var provider = BuildDelegateProvider(o => o.KeyPrefix = null!);

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("KeyPrefix must not be null; use an empty string for no prefix.", exception.Failures);
    }

    [Theory]
    [MemberData(nameof(OutOfRangeDurations))]
    public void DistributedCacheCircuitBreakerDurationOutOfRange_Fails(TimeSpan duration)
    {
        using var provider = BuildDelegateProvider(o => o.DistributedCacheCircuitBreakerDuration = duration);

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("DistributedCacheCircuitBreakerDuration must be between zero and 10 minutes.", exception.Failures);
        Assert.DoesNotContain(exception.Failures, f => f.StartsWith("BackplaneCircuitBreakerDuration", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(OutOfRangeDurations))]
    public void BackplaneCircuitBreakerDurationOutOfRange_Fails(TimeSpan duration)
    {
        using var provider = BuildDelegateProvider(o => o.BackplaneCircuitBreakerDuration = duration);

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RedisL2Options>>().Value);

        Assert.Contains("BackplaneCircuitBreakerDuration must be between zero and 10 minutes.", exception.Failures);
    }

    [Theory]
    [MemberData(nameof(InRangeDurations))]
    public void DurationsInRange_PassValidation(TimeSpan duration)
    {
        using var provider = BuildDelegateProvider(o =>
        {
            o.DistributedCacheCircuitBreakerDuration = duration;
            o.BackplaneCircuitBreakerDuration = duration;
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void ConfigurationOverload_InvalidBoundValues_ReportEveryFailureAtStartup()
    {
        using var provider = BuildConfigurationProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:Redis:L2:KeyPrefix"] = new string('p', 65),
            ["SharedKernel:Caching:Redis:L2:DistributedCacheCircuitBreakerDuration"] = "-00:00:01",
            ["SharedKernel:Caching:Redis:L2:BackplaneCircuitBreakerDuration"] = "00:11:00",
        });

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("KeyPrefix must be at most 64 characters.", exception.Failures);
        Assert.Contains("DistributedCacheCircuitBreakerDuration must be between zero and 10 minutes.", exception.Failures);
        Assert.Contains("BackplaneCircuitBreakerDuration must be between zero and 10 minutes.", exception.Failures);
    }

    // ─── Breaker durations reach FusionCacheOptions ───────────────────────────────

    [Fact]
    public void DefaultDurations_ReachFusionCacheOptions()
    {
        using var provider = BuildDelegateProvider(_ => { });

        var fusion = provider.GetRequiredService<IOptions<FusionCacheOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(2), fusion.DistributedCacheCircuitBreakerDuration);
        Assert.Equal(TimeSpan.FromSeconds(2), fusion.BackplaneCircuitBreakerDuration);
    }

    [Fact]
    public void DelegateDurations_ReachFusionCacheOptions()
    {
        using var provider = BuildDelegateProvider(o =>
        {
            o.DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(9);
            o.BackplaneCircuitBreakerDuration = TimeSpan.Zero;
        });

        var fusion = provider.GetRequiredService<IOptions<FusionCacheOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(9), fusion.DistributedCacheCircuitBreakerDuration);
        Assert.Equal(TimeSpan.Zero, fusion.BackplaneCircuitBreakerDuration);
    }

    [Fact]
    public void BoundDurations_ReachFusionCacheOptions()
    {
        using var provider = BuildConfigurationProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:Redis:L2:DistributedCacheCircuitBreakerDuration"] = "00:00:03",
            ["SharedKernel:Caching:Redis:L2:BackplaneCircuitBreakerDuration"] = "00:01:00",
        });

        var fusion = provider.GetRequiredService<IOptions<FusionCacheOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(3), fusion.DistributedCacheCircuitBreakerDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), fusion.BackplaneCircuitBreakerDuration);
    }

    [Fact]
    public void WithoutAddRedisL2_FusionCacheBreakerDurations_AreUntouched()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc");
        using var provider = services.BuildServiceProvider();

        var fusion = provider.GetRequiredService<IOptions<FusionCacheOptions>>().Value;

        Assert.Equal(new FusionCacheOptions().DistributedCacheCircuitBreakerDuration, fusion.DistributedCacheCircuitBreakerDuration);
        Assert.Equal(new FusionCacheOptions().BackplaneCircuitBreakerDuration, fusion.BackplaneCircuitBreakerDuration);
    }

    private static IConfiguration Configuration(IDictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? new Dictionary<string, string?>()).Build();

    private static ServiceProvider BuildDelegateProvider(Action<RedisL2Options> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");
        services.AddSharedKernelCaching(o => o.ServiceName = "svc").AddRedisL2(configure);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildConfigurationProvider(
        IDictionary<string, string?> values,
        Action<RedisL2Options>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(o => o.ConnectionString = "localhost:6379");
        services.AddSharedKernelCaching(o => o.ServiceName = "svc").AddRedisL2(Configuration(values), configure);
        return services.BuildServiceProvider();
    }
}
