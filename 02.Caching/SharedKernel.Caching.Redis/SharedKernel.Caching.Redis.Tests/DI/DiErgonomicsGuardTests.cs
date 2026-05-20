using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Extensions;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests.DI;

/// <summary>
/// Unit tests for Phase 19 DI ergonomics hardening:
/// <list type="bullet">
///   <item><description>Guard on <c>AddRedisChannelService</c> when <c>IConnectionMultiplexer</c> is absent.</description></item>
///   <item><description>Guards on <c>AddCacheInvalidationReceiver</c> when <c>IRedisChannelService</c> or <c>ICacheService</c> is absent.</description></item>
///   <item><description><c>[Obsolete]</c> <c>AddRedisDistributedLocking(IServiceCollection)</c> shim delegates to the canonical <c>ICachingBuilder</c> overload.</description></item>
///   <item><description>Canonical <c>AddRedisDistributedLocking(ICachingBuilder)</c> registers expected services and returns the builder for chaining.</description></item>
/// </list>
/// </summary>
public sealed class DiErgonomicsGuardTests
{
    // -------------------------------------------------------------------------
    // AddRedisChannelService — IConnectionMultiplexer guard
    // -------------------------------------------------------------------------

    [Fact]
    public void AddRedisChannelService_WithoutMultiplexer_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = new TestCachingBuilder(services);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddRedisChannelService());

        Assert.Equal(
            "AddRedisChannelService requires AddRedisL2 or AddRedisDistributedLocking to be called first to register IConnectionMultiplexer.",
            ex.Message);
    }

    [Fact]
    public void AddRedisChannelService_WithMultiplexerRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        // Register a fake IConnectionMultiplexer to satisfy the guard.
        services.AddSingleton<IConnectionMultiplexer>(_ => null!);
        var builder = new TestCachingBuilder(services);

        // Should not throw.
        var returned = builder.AddRedisChannelService();

        Assert.Same(builder, returned);
        Assert.Single(services, sd => sd.ServiceType == typeof(IRedisChannelService));
    }

    // -------------------------------------------------------------------------
    // AddCacheInvalidationReceiver — IRedisChannelService guard
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheInvalidationReceiver_WithoutChannelService_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        // Register ICacheService but NOT IRedisChannelService.
        services.AddSingleton<ICacheService>(_ => null!);
        var builder = new TestCachingBuilder(services);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddCacheInvalidationReceiver());

        Assert.Equal(
            "AddCacheInvalidationReceiver requires AddRedisChannelService to be called first.",
            ex.Message);
    }

    // -------------------------------------------------------------------------
    // AddCacheInvalidationReceiver — ICacheService guard
    // -------------------------------------------------------------------------

    [Fact]
    public void AddCacheInvalidationReceiver_WithoutCacheService_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        // Register IRedisChannelService but NOT ICacheService.
        services.AddSingleton<IRedisChannelService>(_ => null!);
        var builder = new TestCachingBuilder(services);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddCacheInvalidationReceiver());

        Assert.Equal(
            "AddCacheInvalidationReceiver requires AddSharedKernelCaching to be called first to register ICacheService.",
            ex.Message);
    }

    [Fact]
    public void AddCacheInvalidationReceiver_WithBothDependencies_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRedisChannelService>(_ => null!);
        services.AddSingleton<ICacheService>(_ => null!);
        var builder = new TestCachingBuilder(services);

        // Should not throw — both required registrations are present.
        var returned = builder.AddCacheInvalidationReceiver();

        Assert.Same(builder, returned);
    }

    // -------------------------------------------------------------------------
    // [Obsolete] AddRedisDistributedLocking(IServiceCollection) shim
    // -------------------------------------------------------------------------

    [Fact]
    public void ObsoleteOverload_DelegatesCorrectly_RegistersIDistributedLockService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

#pragma warning disable CS0618 // Type or member is obsolete — deliberate test of shim behaviour
        services.AddRedisDistributedLocking("localhost:6379");
#pragma warning restore CS0618

        // IDistributedLockService should be registered via the canonical ICachingBuilder path.
        Assert.Single(services, sd => sd.ServiceType == typeof(IDistributedLockService));
    }

    [Fact]
    public void ObsoleteOverload_DelegatesCorrectly_RegistersIConnectionMultiplexer()
    {
        var services = new ServiceCollection();
        services.AddLogging();

#pragma warning disable CS0618
        services.AddRedisDistributedLocking("localhost:6379");
#pragma warning restore CS0618

        Assert.Single(services, sd => sd.ServiceType == typeof(IConnectionMultiplexer));
    }

    [Fact]
    public void ObsoleteOverload_ReturnsOriginalServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();

#pragma warning disable CS0618
        var returned = services.AddRedisDistributedLocking("localhost:6379");
#pragma warning restore CS0618

        Assert.Same(services, returned);
    }

    [Fact]
    public void ObsoleteOverload_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? nullServices = null;

#pragma warning disable CS0618
        Assert.Throws<ArgumentNullException>(() =>
            nullServices!.AddRedisDistributedLocking("localhost:6379"));
#pragma warning restore CS0618
    }

    [Fact]
    public void ObsoleteOverload_EmptyConnectionString_ThrowsArgumentException()
    {
        var services = new ServiceCollection();

#pragma warning disable CS0618
        Assert.Throws<ArgumentException>(() => services.AddRedisDistributedLocking(""));
        Assert.Throws<ArgumentException>(() => services.AddRedisDistributedLocking("   "));
#pragma warning restore CS0618
    }

    // -------------------------------------------------------------------------
    // Canonical AddRedisDistributedLocking(ICachingBuilder)
    // -------------------------------------------------------------------------

    [Fact]
    public void CanonicalBuilderOverload_RegistersAllExpectedServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = new TestCachingBuilder(services);

        var returned = builder.AddRedisDistributedLocking("localhost:6379");

        Assert.Same(builder, returned);
        Assert.Single(services, sd => sd.ServiceType == typeof(IConnectionMultiplexer));
        Assert.Single(services, sd => sd.ServiceType == typeof(IDistributedLockService));
    }

    [Fact]
    public void CanonicalBuilderOverload_NullBuilder_ThrowsArgumentNullException()
    {
        ICachingBuilder? nullBuilder = null;

        Assert.Throws<ArgumentNullException>(() =>
            nullBuilder!.AddRedisDistributedLocking("localhost:6379"));
    }

    [Fact]
    public void CanonicalBuilderOverload_EmptyConnectionString_ThrowsArgumentException()
    {
        var services = new ServiceCollection();
        var builder = new TestCachingBuilder(services);

        Assert.Throws<ArgumentException>(() => builder.AddRedisDistributedLocking(""));
        Assert.Throws<ArgumentException>(() => builder.AddRedisDistributedLocking("   "));
    }

    [Fact]
    public void CanonicalBuilderOverload_CalledTwice_DoesNotDuplicateIDistributedLockService()
    {
        var services = new ServiceCollection();
        var builder = new TestCachingBuilder(services);

        builder.AddRedisDistributedLocking("localhost:6379");
        builder.AddRedisDistributedLocking("localhost:6379");

        Assert.Single(services, sd => sd.ServiceType == typeof(IDistributedLockService));
    }

    [Fact]
    public void CanonicalBuilderOverload_AfterAddRedisL2Multiplexer_SharesMultiplexer()
    {
        // When IConnectionMultiplexer is already registered (e.g. by AddRedisL2),
        // AddRedisDistributedLocking must NOT add a second registration (TryAddSingleton).
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionMultiplexer>(_ => null!); // simulate AddRedisL2
        var builder = new TestCachingBuilder(services);

        builder.AddRedisDistributedLocking("localhost:6379");

        // Still only one IConnectionMultiplexer registration.
        Assert.Single(services, sd => sd.ServiceType == typeof(IConnectionMultiplexer));
    }
}
