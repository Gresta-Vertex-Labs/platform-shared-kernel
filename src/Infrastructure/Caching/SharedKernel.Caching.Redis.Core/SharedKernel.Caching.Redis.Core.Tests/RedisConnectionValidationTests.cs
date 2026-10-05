using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Redis.Core.Extensions;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Core.Tests;

/// <summary>
/// <see cref="RedisConnectionOptions"/> validation: data annotations plus <c>RedisConnectionOptionsValidator</c>,
/// surfaced as <see cref="OptionsValidationException"/> at startup and on first options access, for both
/// <c>AddRedisConnection</c> overloads. A failure message never echoes the connection string.
/// </summary>
public sealed class RedisConnectionValidationTests
{
    private const string Password = "Sup3r-S3cret-Pa55";

    public static TheoryData<TimeSpan> OutOfRangeTimeouts => new()
    {
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(-1),
        TimeSpan.FromMilliseconds(99),
        TimeSpan.FromMinutes(1) + TimeSpan.FromMilliseconds(1),
        TimeSpan.FromHours(1),
    };

    public static TheoryData<TimeSpan> InRangeTimeouts => new()
    {
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMinutes(1),
    };

    // ─── Missing connection string ────────────────────────────────────────────────

    [Fact]
    public void ConfigurationOverload_MissingSection_FailsAtStartup()
    {
        using var provider = RedisConnectionOptionsTests.BuildProvider(new Dictionary<string, string?>());

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("ConnectionString is required", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DelegateOverload_BlankConnectionString_FailsAtStartupAndOnOptionsAccess(string connectionString)
    {
        using var provider = BuildDelegateProvider(o => o.ConnectionString = connectionString);

        var startup = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains(nameof(RedisConnectionOptions.ConnectionString), startup.Message);

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<RedisConnectionOptions>>().Value);
    }

    // ─── Unparseable connection string ────────────────────────────────────────────

    [Theory]
    [InlineData("localhost:6379,password=" + Password + ",notAKeyword=1")]
    [InlineData("localhost:6379,password=" + Password + ",connectTimeout=soon")]
    public void UnparseableConnectionString_FailsWithoutEchoingIt(string connectionString)
    {
        using var provider = BuildDelegateProvider(o => o.ConnectionString = connectionString);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("not a valid StackExchange.Redis connection string", exception.Message);
        AssertDoesNotLeak(exception, connectionString);
    }

    [Fact]
    public void ConnectionStringWithoutEndpoint_FailsWithoutEchoingIt()
    {
        var connectionString = "password=" + Password + ",ssl=true";
        using var provider = RedisConnectionOptionsTests.BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:Redis:ConnectionString"] = connectionString,
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RedisConnectionOptions>>().Value);

        Assert.Contains("at least one endpoint", exception.Message);
        AssertDoesNotLeak(exception, connectionString);
    }

    [Fact]
    public void ResolvingTheMultiplexer_WithInvalidOptions_ThrowsOptionsValidationException_NotAConnectionError()
    {
        var connectionString = "password=" + Password;
        using var provider = BuildDelegateProvider(o => o.ConnectionString = connectionString);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IConnectionMultiplexer>());

        AssertDoesNotLeak(exception, connectionString);
    }

    // ─── Timeouts ─────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(OutOfRangeTimeouts))]
    public void DelegateOverload_ConnectTimeoutOutOfRange_Fails(TimeSpan timeout)
    {
        using var provider = BuildDelegateProvider(o =>
        {
            o.ConnectionString = "localhost:6379";
            o.ConnectTimeout = timeout;
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("ConnectTimeout must be between 100 milliseconds and 1 minute.", exception.Failures);
        Assert.DoesNotContain(exception.Failures, f => f.StartsWith("CommandTimeout", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(OutOfRangeTimeouts))]
    public void DelegateOverload_CommandTimeoutOutOfRange_Fails(TimeSpan timeout)
    {
        using var provider = BuildDelegateProvider(o =>
        {
            o.ConnectionString = "localhost:6379";
            o.CommandTimeout = timeout;
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RedisConnectionOptions>>().Value);

        Assert.Contains("CommandTimeout must be between 100 milliseconds and 1 minute.", exception.Failures);
    }

    [Fact]
    public void ConfigurationOverload_BoundTimeoutsOutOfRange_ReportsEveryFailure()
    {
        using var provider = RedisConnectionOptionsTests.BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:Redis:ConnectionString"] = "localhost:6379,password=" + Password,
            ["SharedKernel:Caching:Redis:ConnectTimeout"] = "00:00:00.050",
            ["SharedKernel:Caching:Redis:CommandTimeout"] = "00:02:00",
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("ConnectTimeout must be between 100 milliseconds and 1 minute.", exception.Failures);
        Assert.Contains("CommandTimeout must be between 100 milliseconds and 1 minute.", exception.Failures);
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InRangeTimeouts))]
    public void TimeoutsInRange_PassValidation(TimeSpan timeout)
    {
        using var provider = BuildDelegateProvider(o =>
        {
            o.ConnectionString = "localhost:6379";
            o.ConnectTimeout = timeout;
            o.CommandTimeout = timeout;
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.Equal(timeout, provider.GetRequiredService<IOptions<RedisConnectionOptions>>().Value.ConnectTimeout);
    }

    [Fact]
    public void ValidOptions_WithPasswordAndTls_PassValidation()
    {
        using var provider = RedisConnectionOptionsTests.BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Caching:Redis:ConnectionString"] = "redis-a:6379,redis-b:6379,password=" + Password + ",ssl=true",
        });

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static ServiceProvider BuildDelegateProvider(Action<RedisConnectionOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisConnection(configure);
        return services.BuildServiceProvider();
    }

    private static void AssertDoesNotLeak(OptionsValidationException exception, string connectionString)
    {
        Assert.DoesNotContain(Password, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(connectionString, exception.Message, StringComparison.Ordinal);
        Assert.All(exception.Failures, f => Assert.DoesNotContain(Password, f, StringComparison.Ordinal));
    }
}
