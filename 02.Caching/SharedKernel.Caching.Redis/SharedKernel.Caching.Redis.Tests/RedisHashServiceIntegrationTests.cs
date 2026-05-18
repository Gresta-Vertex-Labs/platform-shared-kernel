using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Extensions;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests;

// ---------------------------------------------------------------------------
// Test model and STJ source-gen context
// ---------------------------------------------------------------------------

/// <summary>Test payload used in hash service tests.</summary>
internal sealed record TestPayload(string Name, int Score);

[JsonSerializable(typeof(TestPayload))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal sealed partial class TestJsonContext : JsonSerializerContext { }

/// <summary>
/// Integration tests for <see cref="IRedisHashService"/> backed by StackExchange.Redis Hash commands.
/// Uses Testcontainers to spin up a real Redis instance.
/// </summary>
[Collection("Redis")]
public sealed class RedisHashServiceIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        // AddRedisDistributedLocking registers IConnectionMultiplexer — required by RedisHashService.
        services.AddRedisDistributedLocking(_redisContainer.GetConnectionString());

        var builder = new TestCachingBuilder(services);
        builder.AddRedisHashService();

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    private IRedisHashService HashService =>
        _provider!.GetRequiredService<IRedisHashService>();

    private static string Key() => "test:hash:" + Guid.NewGuid();

    private static JsonTypeInfo<TestPayload> PayloadTypeInfo =>
        TestJsonContext.Default.TestPayload;

    // ---------------------------------------------------------------------------
    // SetFieldAsync / GetFieldAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SetFieldAsync_GetFieldAsync_RoundTrip_ReturnsStoredValue()
    {
        var key = Key();
        var expected = new TestPayload("Alice", 100);

        await HashService.SetFieldAsync(key, "player", expected, PayloadTypeInfo);
        var actual = await HashService.GetFieldAsync(key, "player", PayloadTypeInfo);

        Assert.NotNull(actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task GetFieldAsync_NonExistentKey_ReturnsDefault()
    {
        var result = await HashService.GetFieldAsync(Key(), "missing-field", PayloadTypeInfo);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetFieldAsync_NonExistentField_ReturnsDefault()
    {
        var key = Key();
        await HashService.SetFieldAsync(key, "field-a", new TestPayload("A", 1), PayloadTypeInfo);

        var result = await HashService.GetFieldAsync(key, "field-b", PayloadTypeInfo);
        Assert.Null(result);
    }

    // ---------------------------------------------------------------------------
    // GetAllFieldsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAllFieldsAsync_MultipleFields_ReturnsAll()
    {
        var key = Key();
        var alice = new TestPayload("Alice", 10);
        var bob = new TestPayload("Bob", 20);

        await HashService.SetFieldAsync(key, "alice", alice, PayloadTypeInfo);
        await HashService.SetFieldAsync(key, "bob", bob, PayloadTypeInfo);

        var all = await HashService.GetAllFieldsAsync(key, PayloadTypeInfo);

        Assert.Equal(2, all.Count);
        Assert.Equal(alice, all["alice"]);
        Assert.Equal(bob, all["bob"]);
    }

    [Fact]
    public async Task GetAllFieldsAsync_NonExistentKey_ReturnsEmptyDictionary()
    {
        var result = await HashService.GetAllFieldsAsync(Key(), PayloadTypeInfo);
        Assert.Empty(result);
    }

    // ---------------------------------------------------------------------------
    // DeleteFieldAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteFieldAsync_ExistingField_RemovesIt()
    {
        var key = Key();
        await HashService.SetFieldAsync(key, "to-delete", new TestPayload("X", 0), PayloadTypeInfo);

        await HashService.DeleteFieldAsync(key, "to-delete");

        var result = await HashService.GetFieldAsync(key, "to-delete", PayloadTypeInfo);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteFieldAsync_NonExistentField_DoesNotThrow()
    {
        // No-op — should not throw.
        await HashService.DeleteFieldAsync(Key(), "ghost-field");
    }

    // ---------------------------------------------------------------------------
    // IncrementFieldAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task IncrementFieldAsync_NewField_StartsAtDelta()
    {
        var key = Key();
        var result = await HashService.IncrementFieldAsync(key, "counter", 5);
        Assert.Equal(5L, result);
    }

    [Fact]
    public async Task IncrementFieldAsync_ExistingField_AccumulatesCorrectly()
    {
        var key = Key();
        await HashService.IncrementFieldAsync(key, "counter", 10);
        var result = await HashService.IncrementFieldAsync(key, "counter", 3);
        Assert.Equal(13L, result);
    }

    [Fact]
    public async Task IncrementFieldAsync_NegativeDelta_Decrements()
    {
        var key = Key();
        await HashService.IncrementFieldAsync(key, "counter", 10);
        var result = await HashService.IncrementFieldAsync(key, "counter", -4);
        Assert.Equal(6L, result);
    }

    // ---------------------------------------------------------------------------
    // Shared multiplexer verification
    // ---------------------------------------------------------------------------

    [Fact]
    public void HashService_SharesMultiplexerSingleton()
    {
        // Both IRedisHashService and IConnectionMultiplexer must resolve to
        // singleton instances, confirming no duplicate connections are created.
        var multiplexer1 = _provider!.GetRequiredService<IConnectionMultiplexer>();
        var multiplexer2 = _provider!.GetRequiredService<IConnectionMultiplexer>();

        Assert.Same(multiplexer1, multiplexer2);

        // IRedisHashService should also be singleton.
        var hash1 = _provider!.GetRequiredService<IRedisHashService>();
        var hash2 = _provider!.GetRequiredService<IRedisHashService>();
        Assert.Same(hash1, hash2);
    }

    // ---------------------------------------------------------------------------
    // AddRedisHashService guard
    // ---------------------------------------------------------------------------

    [Fact]
    public void AddRedisHashService_WithoutMultiplexer_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = new TestCachingBuilder(services);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.AddRedisHashService());
        Assert.Contains("AddRedisHashService requires", ex.Message);
        Assert.Contains("IConnectionMultiplexer", ex.Message);
    }
}
