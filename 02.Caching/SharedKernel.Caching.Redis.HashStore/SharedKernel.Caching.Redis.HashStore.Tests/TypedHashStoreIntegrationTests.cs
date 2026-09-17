using System.Text.Json.Serialization.Metadata;
using NSubstitute;
using SharedKernel.Caching.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.HashStore.Tests;

/// <summary>
/// <see cref="ITypedHashStore{T}"/> passes every call, with its fixed JSON contract, to <see cref="IRedisHashService"/>.
/// </summary>
public sealed class TypedHashStoreDelegationTests
{
    private static readonly JsonTypeInfo<TestPayload> Payload = TestJsonContext.Default.TestPayload;

    private readonly IRedisHashService _inner = Substitute.For<IRedisHashService>();
    private readonly TypedHashStore<TestPayload> _store;
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;

    public TypedHashStoreDelegationTests() => _store = new TypedHashStore<TestPayload>(_inner, Payload);

    [Fact]
    public async Task GetFieldAsync_Delegates()
    {
        var expected = CacheLookup<TestPayload>.Hit(new TestPayload("A", 1));
        _inner.GetFieldAsync("k", "f", Payload, _ct).Returns(expected);

        Assert.Equal(expected, await _store.GetFieldAsync("k", "f", _ct));
    }

    [Fact]
    public async Task GetFieldsAsync_Delegates()
    {
        string[] fields = ["a", "b"];
        IReadOnlyDictionary<string, TestPayload> expected = new Dictionary<string, TestPayload> { ["a"] = new("A", 1) };
        _inner.GetFieldsAsync("k", fields, Payload, _ct).Returns(expected);

        Assert.Same(expected, await _store.GetFieldsAsync("k", fields, _ct));
    }

    [Fact]
    public async Task GetAllFieldsAsync_Delegates()
    {
        IReadOnlyDictionary<string, TestPayload> expected = new Dictionary<string, TestPayload>();
        _inner.GetAllFieldsAsync("k", Payload, _ct).Returns(expected);

        Assert.Same(expected, await _store.GetAllFieldsAsync("k", _ct));
    }

    [Fact]
    public async Task SetFieldAsync_Delegates()
    {
        var value = new TestPayload("A", 1);
        var ttl = TimeSpan.FromSeconds(9);

        await _store.SetFieldAsync("k", "f", value, ttl, _ct);

        await _inner.Received(1).SetFieldAsync("k", "f", value, Payload, ttl, _ct);
    }

    [Fact]
    public async Task SetFieldsAsync_Delegates()
    {
        var values = new Dictionary<string, TestPayload> { ["a"] = new("A", 1) };
        var ttl = TimeSpan.FromSeconds(9);

        await _store.SetFieldsAsync("k", values, ttl, _ct);

        await _inner.Received(1).SetFieldsAsync("k", values, Payload, ttl, _ct);
    }

    [Fact]
    public async Task IncrementFieldAsync_Delegates()
    {
        var ttl = TimeSpan.FromSeconds(9);
        _inner.IncrementFieldAsync("k", "f", 3, ttl, _ct).Returns(42L);

        Assert.Equal(42L, await _store.IncrementFieldAsync("k", "f", 3, ttl, _ct));
    }

    [Fact]
    public async Task DeleteFieldAsync_DeleteAsync_ExpireAsync_Delegate()
    {
        var ttl = TimeSpan.FromSeconds(9);
        _inner.DeleteFieldAsync("k", "f", _ct).Returns(true);
        _inner.DeleteAsync("k", _ct).Returns(true);
        _inner.ExpireAsync("k", ttl, _ct).Returns(true);
        _inner.ExpireAsync("k", null, _ct).Returns(true);

        Assert.True(await _store.DeleteFieldAsync("k", "f", _ct));
        Assert.True(await _store.DeleteAsync("k", _ct));
        Assert.True(await _store.ExpireAsync("k", ttl, _ct));
        Assert.True(await _store.ExpireAsync("k", null, _ct));
    }
}

/// <summary>
/// <see cref="ITypedHashStore{T}"/> registered through <c>AddTypedHashStore</c>, against a real Redis server.
/// </summary>
[Collection("Redis")]
public sealed class TypedHashStoreIntegrationTests(RedisFixture fixture)
{
    private ITypedHashStore<TestPayload> Store => fixture.PayloadStore;

    private IDatabase Database => fixture.Database;

    [Fact]
    public async Task Writes_AreReadableThroughTheHashServiceWithTheSameContract()
    {
        var key = RedisFixture.NewKey("typed-round-trip");
        var first = new TestPayload("first", 10);
        var second = new TestPayload("second", 20);

        await Store.SetFieldAsync(key, "f1", first);
        await Store.SetFieldsAsync(key, new Dictionary<string, TestPayload> { ["f2"] = second });

        Assert.Equal(CacheLookup<TestPayload>.Hit(first), await fixture.HashService.GetFieldAsync(key, "f1", TestJsonContext.Default.TestPayload));
        Assert.Equal(CacheLookup<TestPayload>.Hit(second), await Store.GetFieldAsync(key, "f2"));
        Assert.False((await Store.GetFieldAsync(key, "absent")).IsHit);
        Assert.Equal(2, (await Store.GetAllFieldsAsync(key)).Count);
        Assert.Equal(["f2"], (await Store.GetFieldsAsync(key, ["f2", "f2", "absent"])).Keys);
    }

    [Fact]
    public async Task TimeToLive_DeleteAndExpire_BehaveAsTheHashService()
    {
        var key = RedisFixture.NewKey("typed-ttl");

        await Store.SetFieldAsync(key, "f", new TestPayload("A", 1), TimeSpan.FromSeconds(30));
        Assert.NotNull(await Database.KeyTimeToLiveAsync(key));

        Assert.True(await Store.ExpireAsync(key, null));
        Assert.Null(await Database.KeyTimeToLiveAsync(key));

        Assert.True(await Store.DeleteFieldAsync(key, "f"));
        Assert.False(await Store.DeleteAsync(key));
    }
}
