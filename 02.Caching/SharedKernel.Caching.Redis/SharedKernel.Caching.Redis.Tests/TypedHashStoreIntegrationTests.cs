using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.Extensions;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Tests;

// ---------------------------------------------------------------------------
// Test model and STJ source-gen context
// ---------------------------------------------------------------------------

/// <summary>Test DTO used in ITypedHashStore integration tests.</summary>
internal sealed record TestDto(string Title, int Value);

[JsonSerializable(typeof(TestDto))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal sealed partial class TestDtoJsonContext : JsonSerializerContext { }

/// <summary>
/// Integration tests for <see cref="ITypedHashStore{T}"/> backed by Redis via Testcontainers.
/// Verifies all five methods: SetField, GetField, GetAllFields, DeleteField, and IncrementField.
/// </summary>
[Collection("Redis")]
public sealed class TypedHashStoreIntegrationTests : IAsyncLifetime
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
        builder
            .AddRedisHashService()
            .AddTypedHashStore(TestDtoJsonContext.Default.TestDto);

        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _redisContainer.DisposeAsync();
    }

    private ITypedHashStore<TestDto> Store =>
        _provider!.GetRequiredService<ITypedHashStore<TestDto>>();

    private static string Key() => "typed:hash:" + Guid.NewGuid();

    // ---------------------------------------------------------------------------
    // SetFieldAsync / GetFieldAsync round-trip
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SetFieldAsync_GetFieldAsync_RoundTrip_ReturnsStoredValue()
    {
        var key = Key();
        var expected = new TestDto("hello", 42);

        await Store.SetFieldAsync(key, "item", expected);
        var actual = await Store.GetFieldAsync(key, "item");

        Assert.NotNull(actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task GetFieldAsync_NonExistentKey_ReturnsNull()
    {
        var result = await Store.GetFieldAsync(Key(), "missing");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetFieldAsync_NonExistentField_ReturnsNull()
    {
        var key = Key();
        await Store.SetFieldAsync(key, "field-a", new TestDto("A", 1));

        var result = await Store.GetFieldAsync(key, "field-b");
        Assert.Null(result);
    }

    // ---------------------------------------------------------------------------
    // GetAllFieldsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAllFieldsAsync_MultipleFields_ReturnsAll()
    {
        var key = Key();
        var first = new TestDto("first", 10);
        var second = new TestDto("second", 20);

        await Store.SetFieldAsync(key, "f1", first);
        await Store.SetFieldAsync(key, "f2", second);

        var all = await Store.GetAllFieldsAsync(key);

        Assert.Equal(2, all.Count);
        Assert.Equal(first, all["f1"]);
        Assert.Equal(second, all["f2"]);
    }

    [Fact]
    public async Task GetAllFieldsAsync_NonExistentKey_ReturnsEmptyDictionary()
    {
        var result = await Store.GetAllFieldsAsync(Key());
        Assert.Empty(result);
    }

    // ---------------------------------------------------------------------------
    // DeleteFieldAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteFieldAsync_ExistingField_RemovesIt()
    {
        var key = Key();
        await Store.SetFieldAsync(key, "to-delete", new TestDto("X", 0));

        await Store.DeleteFieldAsync(key, "to-delete");

        var result = await Store.GetFieldAsync(key, "to-delete");
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteFieldAsync_NonExistentField_DoesNotThrow()
    {
        // No-op — must not throw.
        await Store.DeleteFieldAsync(Key(), "ghost");
    }

    // ---------------------------------------------------------------------------
    // IncrementFieldAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task IncrementFieldAsync_NewField_StartsAtDelta()
    {
        var key = Key();
        var result = await Store.IncrementFieldAsync(key, "counter", 5);
        Assert.Equal(5L, result);
    }

    [Fact]
    public async Task IncrementFieldAsync_ExistingField_Accumulates()
    {
        var key = Key();
        await Store.IncrementFieldAsync(key, "counter", 10);
        var result = await Store.IncrementFieldAsync(key, "counter", 3);
        Assert.Equal(13L, result);
    }

    [Fact]
    public async Task IncrementFieldAsync_NegativeDelta_Decrements()
    {
        var key = Key();
        await Store.IncrementFieldAsync(key, "counter", 10);
        var result = await Store.IncrementFieldAsync(key, "counter", -4);
        Assert.Equal(6L, result);
    }

    // ---------------------------------------------------------------------------
    // DI registration guard
    // ---------------------------------------------------------------------------

    [Fact]
    public void AddTypedHashStore_WithoutRedisHashService_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = new TestCachingBuilder(services);

        // IRedisHashService is not registered — guard must fire.
        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddTypedHashStore(TestDtoJsonContext.Default.TestDto));

        Assert.Contains("AddTypedHashStore", ex.Message);
        Assert.Contains("AddRedisHashService", ex.Message);
    }

    [Fact]
    public void AddTypedHashStore_IsSingleton_ReturnsSameInstance()
    {
        var instance1 = _provider!.GetRequiredService<ITypedHashStore<TestDto>>();
        var instance2 = _provider!.GetRequiredService<ITypedHashStore<TestDto>>();
        Assert.Same(instance1, instance2);
    }
}
