using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Redis.Core.Extensions;
using Xunit;

namespace SharedKernel.Caching.Redis.Core.Tests;

/// <summary>
/// <see cref="RedisConnectionOptions"/> defaults and binding from the <c>SharedKernel:Caching:Redis</c> section.
/// </summary>
public sealed class RedisConnectionOptionsTests
{
    [Fact]
    public void SectionName_IsSharedKernelCachingRedis() =>
        Assert.Equal("SharedKernel:Caching:Redis", RedisConnectionOptions.SectionName);

    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new RedisConnectionOptions();

        Assert.Equal(string.Empty, options.ConnectionString);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.CommandTimeout);
        Assert.True(options.FailFastWhenDisconnected);
        Assert.False(options.Ssl);
        Assert.Null(options.ClientCertificates);
        Assert.Null(options.CertificateValidation);
    }

    [Fact]
    public void ConfigurationOverload_BindsEverySetting()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:Redis:ConnectionString"] = "redis.internal:6380",
            ["SharedKernel:Caching:Redis:ConnectTimeout"] = "00:00:02",
            ["SharedKernel:Caching:Redis:CommandTimeout"] = "00:00:00.750",
            ["SharedKernel:Caching:Redis:FailFastWhenDisconnected"] = "false",
            ["SharedKernel:Caching:Redis:Ssl"] = "true",
        });

        var options = provider.GetRequiredService<IOptions<RedisConnectionOptions>>().Value;

        Assert.Equal("redis.internal:6380", options.ConnectionString);
        Assert.Equal(TimeSpan.FromSeconds(2), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(750), options.CommandTimeout);
        Assert.False(options.FailFastWhenDisconnected);
        Assert.True(options.Ssl);
    }

    [Fact]
    public void ConfigurationOverload_UnsetSettings_KeepDefaults()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:Redis:ConnectionString"] = "localhost:6379",
        });

        var options = provider.GetRequiredService<IOptions<RedisConnectionOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(5), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.CommandTimeout);
        Assert.True(options.FailFastWhenDisconnected);
        Assert.False(options.Ssl);
    }

    [Fact]
    public void ConfigurationOverload_OtherSections_AreIgnored()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Redis:ConnectionString"] = "wrong-section:6379",
            ["SharedKernel:Caching:ConnectionString"] = "parent-section:6379",
            ["SharedKernel:Caching:Redis:ConnectionString"] = "right-section:6379",
        });

        Assert.Equal(
            "right-section:6379",
            provider.GetRequiredService<IOptions<RedisConnectionOptions>>().Value.ConnectionString);
    }

    [Fact]
    public void ConfigurationOverload_DelegateRunsAfterBinding()
    {
        using var provider = BuildProvider(
            new Dictionary<string, string?>
            {
                ["SharedKernel:Caching:Redis:ConnectionString"] = "from-config:6379",
                ["SharedKernel:Caching:Redis:ConnectTimeout"] = "00:00:02",
                ["SharedKernel:Caching:Redis:CommandTimeout"] = "00:00:03",
            },
            o =>
            {
                // Sees the bound value, then overrides some settings.
                Assert.Equal("from-config:6379", o.ConnectionString);
                o.ConnectionString = "from-delegate:6379";
                o.CommandTimeout = TimeSpan.FromSeconds(4);
            });

        var options = provider.GetRequiredService<IOptions<RedisConnectionOptions>>().Value;

        Assert.Equal("from-delegate:6379", options.ConnectionString);
        Assert.Equal(TimeSpan.FromSeconds(2), options.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(4), options.CommandTimeout);
    }

    [Fact]
    public void ConfigurationOverload_DelegateCanSetTheConnectionStringWhenTheSectionIsMissing()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>(), o => o.ConnectionString = "localhost:6379");

        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.Equal("localhost:6379", provider.GetRequiredService<IOptions<RedisConnectionOptions>>().Value.ConnectionString);
    }

    internal static ServiceProvider BuildProvider(
        IDictionary<string, string?> values,
        Action<RedisConnectionOptions>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(configuration, configure);
        return services.BuildServiceProvider();
    }
}
