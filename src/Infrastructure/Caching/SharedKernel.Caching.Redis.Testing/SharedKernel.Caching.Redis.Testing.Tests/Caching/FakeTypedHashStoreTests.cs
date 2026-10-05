using System.Text.Json;
using SharedKernel.Testing.Caching;
using Xunit;
using ManualTimeProvider = SharedKernel.Testing.SelfTests.Caching.FakeRedisHashServiceTests.ManualTimeProvider;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>Test payload used by <see cref="FakeTypedHashStoreTests"/>.</summary>
internal sealed record FakeTypedHashStoreTestDto(string Name, int Value);

/// <summary>Proves <see cref="FakeTypedHashStore{T}"/> against <c>ITypedHashStore{T}</c>'s contract.</summary>
public sealed class FakeTypedHashStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SetFieldAsync_ThenGetFieldAsync_ReturnsHit()
    {
        var store = new FakeTypedHashStore<FakeTypedHashStoreTestDto>();
        var dto = new FakeTypedHashStoreTestDto("Alice", 42);

        await store.SetFieldAsync("hash-1", "field-1", dto);

        var lookup = await store.GetFieldAsync("hash-1", "field-1");
        Assert.True(lookup.IsHit);
        Assert.Equal(dto, lookup.Value);
        Assert.False((await store.GetFieldAsync("hash-1", "missing")).IsHit);
    }

    [Fact]
    public async Task StoredNull_IsAHit()
    {
        var store = new FakeTypedHashStore<string?>();

        await store.SetFieldAsync("hash-1", "field-1", null);

        var lookup = await store.GetFieldAsync("hash-1", "field-1");
        Assert.True(lookup.IsHit);
        Assert.Null(lookup.Value);
    }

    [Fact]
    public async Task GetFieldsAsync_And_GetAllFieldsAsync_ReturnExistingFields()
    {
        var store = new FakeTypedHashStore<string>();
        await store.SetFieldsAsync("hash-1", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        await store.SetFieldAsync("hash-2", "c", "3");

        Assert.Equal(new Dictionary<string, string> { ["b"] = "2" }, await store.GetFieldsAsync("hash-1", ["b", "b", "x"]));
        Assert.Equal(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }, await store.GetAllFieldsAsync("hash-1"));
        Assert.Empty(await store.GetAllFieldsAsync("missing"));
    }

    [Fact]
    public async Task SetFieldsAsync_EmptyValues_ThrowsArgumentException()
    {
        var store = new FakeTypedHashStore<string>();

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.SetFieldsAsync("hash-1", new Dictionary<string, string>()));
    }

    [Fact]
    public async Task IncrementFieldAsync_OnLongStore_CountsAndReadsBack()
    {
        var store = new FakeTypedHashStore<long>();
        await store.SetFieldAsync("counters", "hits", 10);

        Assert.Equal(12, await store.IncrementFieldAsync("counters", "hits", 2));
        Assert.Equal(1, await store.IncrementFieldAsync("counters", "new"));
        Assert.Equal(12L, (await store.GetFieldAsync("counters", "hits")).Value);
    }

    [Fact]
    public async Task IncrementFieldAsync_NonIntegerField_ThrowsInvalidOperation()
    {
        var store = new FakeTypedHashStore<string>();
        await store.SetFieldAsync("hash-1", "name", "Alice");

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.IncrementFieldAsync("hash-1", "name"));
    }

    [Fact]
    public async Task CounterField_ReadFromNonLongStore_ThrowsJsonException()
    {
        var store = new FakeTypedHashStore<FakeTypedHashStoreTestDto>();
        await store.IncrementFieldAsync("hash-1", "hits");

        await Assert.ThrowsAsync<JsonException>(async () => await store.GetFieldAsync("hash-1", "hits"));
    }

    [Fact]
    public async Task DeleteFieldAsync_And_DeleteAsync_ReturnWhetherSomethingExisted()
    {
        var store = new FakeTypedHashStore<string>();
        await store.SetFieldsAsync("hash-1", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });

        Assert.True(await store.DeleteFieldAsync("hash-1", "a"));
        Assert.False(await store.DeleteFieldAsync("hash-1", "a"));
        Assert.True(await store.DeleteAsync("hash-1"));
        Assert.False(await store.DeleteAsync("hash-1"));
    }

    [Fact]
    public async Task TimeToLive_ExpiresHash_AndExpireAsyncPersists()
    {
        var time = new ManualTimeProvider(Start);
        var store = new FakeTypedHashStore<string>(time);

        await store.SetFieldAsync("session", "a", "1", TimeSpan.FromMinutes(5));
        await store.SetFieldAsync("other", "a", "1", TimeSpan.FromMinutes(5));
        Assert.True(await store.ExpireAsync("other", null));
        Assert.Equal(TimeSpan.FromMinutes(5), store.GetTimeToLive("session"));

        time.Advance(TimeSpan.FromMinutes(5));

        Assert.False((await store.GetFieldAsync("session", "a")).IsHit);
        Assert.False(await store.ExpireAsync("session", TimeSpan.FromMinutes(1)));
        Assert.True(store.ContainsKey("other"));
        Assert.Equal(["other"], store.Keys);
    }

    [Fact]
    public async Task InvalidArguments_Throw_LikeTheRealStore()
    {
        var store = new FakeTypedHashStore<string>();

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await store.GetFieldAsync(" ", "f"));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await store.SetFieldAsync("k", "", "v"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await store.SetFieldAsync("k", "f", "v", TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await store.ExpireAsync("k", TimeSpan.FromSeconds(-1)));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.GetFieldsAsync("k", null!));
    }

    [Fact]
    public async Task SimulateFailure_ThrowsTimeout_AndSeedBypassesIt()
    {
        var store = new FakeTypedHashStore<long> { SimulateFailure = true };
        store.Seed("counters", "hits", 3);

        await Assert.ThrowsAsync<TimeoutException>(async () => await store.GetFieldAsync("counters", "hits"));
        await Assert.ThrowsAsync<TimeoutException>(async () => await store.IncrementFieldAsync("counters", "hits"));
        await Assert.ThrowsAsync<TimeoutException>(async () => await store.DeleteAsync("counters"));

        store.SimulateFailure = false;
        Assert.Equal(3L, (await store.GetFieldAsync("counters", "hits")).Value);

        store.Reset();
        Assert.Empty(store.Keys);
    }

    [Fact]
    public async Task Instance_MaintainsStateIndependently_FromFakeRedisHashService()
    {
        var typedStore = new FakeTypedHashStore<string>();
        var hashService = new FakeRedisHashService();

        await typedStore.SetFieldAsync("hash-1", "field-1", "typed-value");
        hashService.SeedRaw("hash-1", "field-1", "12345");

        Assert.Equal("typed-value", (await typedStore.GetFieldAsync("hash-1", "field-1")).Value);
    }
}
