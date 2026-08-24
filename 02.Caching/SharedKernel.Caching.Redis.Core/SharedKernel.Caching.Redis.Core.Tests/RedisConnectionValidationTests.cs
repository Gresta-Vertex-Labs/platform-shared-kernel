using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Redis.Core.Extensions;
using Xunit;

namespace SharedKernel.Caching.Redis.Core.Tests;

/// <summary>
/// Unit tests for <see cref="RedisConnectionOptions"/>'s fail-fast <c>ValidateDataAnnotations()</c>/
/// <c>ValidateOnStart()</c> wiring inside <see cref="RedisConnectionCoreExtensions.AddRedisConnection"/>.
/// Covers TH-06.
/// </summary>
public sealed class RedisConnectionValidationTests
{
    [Fact]
    public void AddRedisConnection_InvalidConnectTimeoutMs_ThrowsOptionsValidationException_AtStartupValidation()
    {
        var services = new ServiceCollection();

        services.AddRedisConnection("localhost:6379", o => o.ConnectTimeoutMs = -1);

        var provider = services.BuildServiceProvider();
        var startupValidator = provider.GetRequiredService<IStartupValidator>();

        var exception = Assert.Throws<OptionsValidationException>(() => startupValidator.Validate());
        Assert.Contains(nameof(RedisConnectionOptions.ConnectTimeoutMs), exception.Message);
    }

    [Fact]
    public void AddRedisConnection_WhitespaceConnectionStringViaConfigure_ThrowsOptionsValidationException_AtStartupValidation()
    {
        var services = new ServiceCollection();

        // The bare-connection-string parameter itself is guarded by ArgumentException.ThrowIfNullOrWhiteSpace
        // (unchanged, pre-Phase-45 behavior). This test drives an invalid ConnectionString through the
        // configure delegate instead, which is not caught by that eager guard and must be caught by
        // ValidateDataAnnotations() instead.
        services.AddRedisConnection("localhost:6379", o => o.ConnectionString = "   ");

        var provider = services.BuildServiceProvider();
        var startupValidator = provider.GetRequiredService<IStartupValidator>();

        var exception = Assert.Throws<OptionsValidationException>(() => startupValidator.Validate());
        Assert.Contains(nameof(RedisConnectionOptions.ConnectionString), exception.Message);
    }

    [Fact]
    public void AddRedisConnection_InvalidOptions_ThrowsOptionsValidationException_OnFirstOptionsAccess()
    {
        // Proves the same failure surfaces as a clear OptionsValidationException the moment
        // RedisConnectionOptions is first read — e.g. inside the IConnectionMultiplexer singleton
        // factory — rather than an unvalidated bad value flowing into StackExchange.Redis and
        // surfacing later as an opaque RedisConnectionException.
        var services = new ServiceCollection();

        services.AddRedisConnection("localhost:6379", o => o.ConnectTimeoutMs = 100_000);

        var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<RedisConnectionOptions>>().Value);
    }

    [Fact]
    public void AddRedisConnection_ValidOptions_PassesStartupValidation()
    {
        var services = new ServiceCollection();

        services.AddRedisConnection("localhost:6379", o => o.ConnectTimeoutMs = 1_234);

        var provider = services.BuildServiceProvider();
        var startupValidator = provider.GetRequiredService<IStartupValidator>();

        // Should not throw.
        startupValidator.Validate();
    }

    [Fact]
    public void AddRedisConnection_DefaultOptions_PassValidation()
    {
        // Regression: a bare connection-string call site with no configure delegate at all must
        // remain valid — TH-09's backward-compatibility guarantee, checked from the validation side.
        var services = new ServiceCollection();

        services.AddRedisConnection("localhost:6379");

        var provider = services.BuildServiceProvider();
        var startupValidator = provider.GetRequiredService<IStartupValidator>();

        startupValidator.Validate();
    }
}
