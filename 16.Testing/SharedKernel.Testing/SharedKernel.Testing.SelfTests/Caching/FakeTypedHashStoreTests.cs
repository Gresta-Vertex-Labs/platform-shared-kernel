using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>Test payload used by <see cref="FakeTypedHashStoreTests"/>.</summary>
internal sealed record FakeTypedHashStoreTestDto(string Name, int Value);

/// <summary>
/// Proves <see cref="FakeTypedHashStore{T}"/> against <c>ITypedHashStore{T}</c>'s contract — no
/// existing `02.Caching` consuming-domain test currently exercises this fake directly, so coverage
/// is provided here per the documented SelfTests fallback.
/// </summary>
public sealed class FakeTypedHashStoreTests
{
    [Fact]
    public async Task SetFieldAsync_ThenGetFieldAsync_RoundTrips()
    {
        var store = new FakeTypedHashStore<FakeTypedHashStoreTestDto>();
        var dto = new FakeTypedHashStoreTestDto("Alice", 42);

        await store.SetFieldAsync("hash-1", "field-1", dto);
        var result = await store.GetFieldAsync("hash-1", "field-1");

        Assert.Equal(dto, result);
    }

    [Fact]
    public async Task GetFieldAsync_MissingField_ReturnsDefault()
    {
        var store = new FakeTypedHashStore<FakeTypedHashStoreTestDto>();

        var result = await store.GetFieldAsync("hash-1", "missing");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllFieldsAsync_ExcludesFieldsUnderOtherKeys()
    {
        var store = new FakeTypedHashStore<string>();
        await store.SetFieldAsync("hash-1", "field-1", "value-1");
        await store.SetFieldAsync("hash-2", "field-1", "value-2");

        var result = await store.GetAllFieldsAsync("hash-1");

        Assert.Single(result);
        Assert.Equal("value-1", result["field-1"]);
    }

    [Fact]
    public async Task DeleteFieldAsync_RemovesField()
    {
        var store = new FakeTypedHashStore<string>();
        await store.SetFieldAsync("hash-1", "field-1", "value");

        await store.DeleteFieldAsync("hash-1", "field-1");

        Assert.Null(await store.GetFieldAsync("hash-1", "field-1"));
    }

    [Fact]
    public async Task DeleteFieldAsync_MissingField_IsIdempotentNoOp()
    {
        var store = new FakeTypedHashStore<string>();

        var exception = await Record.ExceptionAsync(async () => await store.DeleteFieldAsync("hash-1", "missing"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task IncrementFieldAsync_MissingField_TreatedAsZero()
    {
        var store = new FakeTypedHashStore<long>();

        var result = await store.IncrementFieldAsync("hash-1", "counter", 5);

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task IncrementFieldAsync_ExistingField_AddsDelta()
    {
        var store = new FakeTypedHashStore<long>();
        await store.IncrementFieldAsync("hash-1", "counter", 5);

        var result = await store.IncrementFieldAsync("hash-1", "counter", 3);

        Assert.Equal(8, result);
    }

    [Fact]
    public async Task IncrementFieldAsync_NonLongField_ThrowsInvalidCastException()
    {
        var store = new FakeTypedHashStore<string>();
        store.Seed("hash-1", "field-1", "not-a-long");

        await Assert.ThrowsAsync<InvalidCastException>(async () => await store.IncrementFieldAsync("hash-1", "field-1", 1));
    }

    [Fact]
    public async Task Seed_PrePopulatesField_WithoutGoingThroughSetFieldAsync()
    {
        var store = new FakeTypedHashStore<string>();
        store.Seed("hash-1", "field-1", "seeded-value");

        var result = await store.GetFieldAsync("hash-1", "field-1");

        Assert.Equal("seeded-value", result);
    }

    [Fact]
    public async Task Reset_ClearsAllStoredFields()
    {
        var store = new FakeTypedHashStore<string>();
        await store.SetFieldAsync("hash-1", "field-1", "value");

        store.Reset();

        Assert.Null(await store.GetFieldAsync("hash-1", "field-1"));
    }

    [Fact]
    public async Task SimulateFailure_GetFieldAsync_Throws()
    {
        var store = new FakeTypedHashStore<string> { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.GetFieldAsync("hash-1", "field-1"));
    }

    [Fact]
    public async Task SimulateFailure_SetFieldAsync_Throws()
    {
        var store = new FakeTypedHashStore<string> { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.SetFieldAsync("hash-1", "field-1", "value"));
    }

    [Fact]
    public async Task SimulateFailure_GetAllFieldsAsync_Throws()
    {
        var store = new FakeTypedHashStore<string> { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.GetAllFieldsAsync("hash-1"));
    }

    [Fact]
    public async Task SimulateFailure_DeleteFieldAsync_Throws()
    {
        var store = new FakeTypedHashStore<string> { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.DeleteFieldAsync("hash-1", "field-1"));
    }

    [Fact]
    public async Task SimulateFailure_IncrementFieldAsync_Throws()
    {
        var store = new FakeTypedHashStore<long> { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.IncrementFieldAsync("hash-1", "field-1", 1));
    }

    [Fact]
    public async Task Instance_MaintainsStateIndependently_FromSeparatelyConstructedFakeRedisHashService()
    {
        var typedStore = new FakeTypedHashStore<string>();
        var hashService = new FakeRedisHashService();

        await typedStore.SetFieldAsync("hash-1", "field-1", "typed-value");
        // Seed the same (key, field) pair on the separately-constructed FakeRedisHashService with an
        // incompatible value — if the two fakes shared a backing store this would corrupt the read
        // below.
        hashService.Seed("hash-1", "field-1", 12345);

        Assert.Equal("typed-value", await typedStore.GetFieldAsync("hash-1", "field-1"));
    }
}
