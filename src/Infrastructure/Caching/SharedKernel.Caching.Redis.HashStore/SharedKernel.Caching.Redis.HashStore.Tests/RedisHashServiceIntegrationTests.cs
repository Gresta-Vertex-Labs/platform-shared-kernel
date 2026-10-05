using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Caching.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.HashStore.Tests;

/// <summary>
/// <see cref="IRedisHashService"/> against a real Redis server, verified by inspecting the keys directly.
/// </summary>
[Collection("Redis")]
public sealed class RedisHashServiceIntegrationTests(RedisFixture fixture)
{
    private static readonly JsonTypeInfo<TestPayload> Payload = TestJsonContext.Default.TestPayload;
    private static readonly JsonTypeInfo<int> Int32 = TestJsonContext.Default.Int32;

    private IRedisHashService HashService => fixture.HashService;

    private IDatabase Database => fixture.Database;

    // -------------------------------------------------------------------------
    // GetFieldAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetFieldAsync_ThenGetFieldAsync_ReturnsHitWithStoredValue()
    {
        var key = RedisFixture.NewKey("round-trip");
        var expected = new TestPayload("Alice", 100);

        await HashService.SetFieldAsync(key, "player", expected, Payload);
        var lookup = await HashService.GetFieldAsync(key, "player", Payload);

        Assert.Equal(CacheLookup<TestPayload>.Hit(expected), lookup);
        Assert.Equal("{\"Name\":\"Alice\",\"Score\":100}", (string?)await Database.HashGetAsync(key, "player"));
    }

    [Fact]
    public async Task GetFieldAsync_MissingKeyOrField_ReturnsMiss()
    {
        var key = RedisFixture.NewKey("miss");
        Assert.False((await HashService.GetFieldAsync(key, "absent", Payload)).IsHit);

        await HashService.SetFieldAsync(key, "present", new TestPayload("A", 1), Payload);

        var lookup = await HashService.GetFieldAsync(key, "absent", Payload);
        Assert.False(lookup.IsHit);
        Assert.Equal(CacheLookup<TestPayload>.Miss, lookup);
    }

    [Fact]
    public async Task GetFieldAsync_Int32_MissingFieldIsMiss_StoredZeroIsHitZero()
    {
        var key = RedisFixture.NewKey("zero");

        var missing = await HashService.GetFieldAsync(key, "count", Int32);
        Assert.False(missing.IsHit);
        Assert.False(missing.TryGetValue(out _));

        await HashService.SetFieldAsync(key, "count", 0, Int32);
        var stored = await HashService.GetFieldAsync(key, "count", Int32);

        Assert.True(stored.IsHit);
        Assert.True(stored.TryGetValue(out var value));
        Assert.Equal(0, value);
        Assert.Equal(CacheLookup<int>.Hit(0), stored);
        Assert.NotEqual(missing, stored);
    }

    [Fact]
    public async Task GetFieldAsync_IncrementedField_ReadsAsInt32()
    {
        var key = RedisFixture.NewKey("increment-read");

        await HashService.IncrementFieldAsync(key, "count", 42);

        Assert.Equal(CacheLookup<int>.Hit(42), await HashService.GetFieldAsync(key, "count", Int32));
    }

    [Fact]
    public async Task GetFieldAsync_CorruptStoredValue_ThrowsJsonException()
    {
        var key = RedisFixture.NewKey("corrupt");
        await Database.HashSetAsync(key, "player", "{not json");

        await Assert.ThrowsAnyAsync<JsonException>(() => HashService.GetFieldAsync(key, "player", Payload).AsTask());
    }

    [Fact]
    public async Task GetFieldAsync_StoredValueOfAnotherType_ThrowsJsonException()
    {
        var key = RedisFixture.NewKey("wrong-type");
        await HashService.SetFieldAsync(key, "field", "text", TestJsonContext.Default.String);

        await Assert.ThrowsAnyAsync<JsonException>(() => HashService.GetFieldAsync(key, "field", Int32).AsTask());
    }

    // -------------------------------------------------------------------------
    // GetFieldsAsync / GetAllFieldsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetFieldsAsync_ReturnsOnlyFieldsThatExist()
    {
        var key = RedisFixture.NewKey("get-many");
        var alice = new TestPayload("Alice", 1);
        var bob = new TestPayload("Bob", 2);
        await HashService.SetFieldsAsync(key, new Dictionary<string, TestPayload> { ["alice"] = alice, ["bob"] = bob }, Payload);

        var result = await HashService.GetFieldsAsync(key, ["alice", "carol", "bob", "dave"], Payload);

        Assert.Equal(2, result.Count);
        Assert.Equal(alice, result["alice"]);
        Assert.Equal(bob, result["bob"]);
        Assert.False(result.ContainsKey("carol"));
    }

    [Fact]
    public async Task GetFieldsAsync_DuplicateFieldNames_AreReadOnce()
    {
        var key = RedisFixture.NewKey("get-many-duplicates");
        await HashService.SetFieldsAsync(key, new Dictionary<string, int> { ["a"] = 1, ["b"] = 0 }, Int32);

        var result = await HashService.GetFieldsAsync(key, ["a", "a", "b", "a", "b"], Int32);

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result["a"]);
        Assert.Equal(0, result["b"]);
    }

    [Fact]
    public async Task GetFieldsAsync_FieldNamesAreCaseSensitive()
    {
        var key = RedisFixture.NewKey("get-many-case");
        await HashService.SetFieldAsync(key, "Field", 1, Int32);

        var result = await HashService.GetFieldsAsync(key, ["Field", "field"], Int32);

        Assert.Equal(1, Assert.Single(result).Value);
        Assert.Equal("Field", Assert.Single(result).Key);
    }

    [Fact]
    public async Task GetFieldsAsync_MissingKeyOrNoFields_ReturnsEmpty()
    {
        var key = RedisFixture.NewKey("get-many-empty");

        Assert.Empty(await HashService.GetFieldsAsync(key, ["a", "b"], Int32));
        Assert.Empty(await HashService.GetFieldsAsync(key, [], Int32));
    }

    [Fact]
    public async Task GetFieldsAsync_CorruptStoredValue_ThrowsJsonException()
    {
        var key = RedisFixture.NewKey("get-many-corrupt");
        await Database.HashSetAsync(key, [new HashEntry("good", "1"), new HashEntry("bad", "{")]);

        await Assert.ThrowsAnyAsync<JsonException>(() => HashService.GetFieldsAsync(key, ["good", "bad"], Int32).AsTask());
    }

    [Fact]
    public async Task GetAllFieldsAsync_ReturnsEveryField_AndEmptyForMissingKey()
    {
        var key = RedisFixture.NewKey("get-all");
        var alice = new TestPayload("Alice", 10);
        var bob = new TestPayload("Bob", 20);
        await HashService.SetFieldAsync(key, "alice", alice, Payload);
        await HashService.SetFieldAsync(key, "bob", bob, Payload);

        var all = await HashService.GetAllFieldsAsync(key, Payload);

        Assert.Equal(2, all.Count);
        Assert.Equal(alice, all["alice"]);
        Assert.Equal(bob, all["bob"]);
        Assert.Empty(await HashService.GetAllFieldsAsync(RedisFixture.NewKey("get-all-missing"), Payload));
    }

    [Fact]
    public async Task GetAllFieldsAsync_CorruptStoredValue_ThrowsJsonException()
    {
        var key = RedisFixture.NewKey("get-all-corrupt");
        await Database.HashSetAsync(key, "bad", "nope");

        await Assert.ThrowsAnyAsync<JsonException>(() => HashService.GetAllFieldsAsync(key, Int32).AsTask());
    }

    // -------------------------------------------------------------------------
    // SetFieldsAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetFieldsAsync_WritesEveryField_AndOverwritesExistingOnes()
    {
        var key = RedisFixture.NewKey("set-many");
        await HashService.SetFieldAsync(key, "keep", 7, Int32);
        await HashService.SetFieldAsync(key, "a", 100, Int32);

        await HashService.SetFieldsAsync(key, new Dictionary<string, int> { ["a"] = 1, ["b"] = 2, ["c"] = 0 }, Int32);

        var all = await HashService.GetAllFieldsAsync(key, Int32);
        Assert.Equal(
            new Dictionary<string, int> { ["keep"] = 7, ["a"] = 1, ["b"] = 2, ["c"] = 0 },
            all.OrderBy(p => p.Key).ToDictionary());
        Assert.Null(await Database.KeyTimeToLiveAsync(key));
    }

    [Fact]
    public async Task SetFieldsAsync_Empty_ThrowsArgumentException_AndWritesNothing()
    {
        var key = RedisFixture.NewKey("set-many-empty");

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => HashService.SetFieldsAsync(key, new Dictionary<string, int>(), Int32, TimeSpan.FromMinutes(1)).AsTask());

        Assert.Equal("values", ex.ParamName);
        Assert.False(await Database.KeyExistsAsync(key));
    }

    // -------------------------------------------------------------------------
    // Time to live
    // -------------------------------------------------------------------------

    public static TheoryData<string> WriteOperations => new() { "SetField", "SetFields", "IncrementField" };

    [Theory]
    [MemberData(nameof(WriteOperations))]
    public async Task Write_WithTimeToLive_SetsTheKeysExpiry(string operation)
    {
        var key = RedisFixture.NewKey("ttl-" + operation);
        var timeToLive = TimeSpan.FromSeconds(30);

        await WriteAsync(operation, key, timeToLive);

        var ttl = await Database.KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, timeToLive - TimeSpan.FromSeconds(5), timeToLive);
    }

    [Theory]
    [MemberData(nameof(WriteOperations))]
    public async Task Write_WithoutTimeToLive_LeavesNoExpiryOnANewKey(string operation)
    {
        var key = RedisFixture.NewKey("no-ttl-" + operation);

        await WriteAsync(operation, key, timeToLive: null);

        Assert.True(await Database.KeyExistsAsync(key));
        Assert.Null(await Database.KeyTimeToLiveAsync(key));
    }

    [Theory]
    [MemberData(nameof(WriteOperations))]
    public async Task Write_WithTimeToLive_RestartsTheExpiryOnEachWrite(string operation)
    {
        var key = RedisFixture.NewKey("ttl-restart-" + operation);
        var timeToLive = TimeSpan.FromSeconds(3);

        await WriteAsync(operation, key, timeToLive);
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await WriteAsync(operation, key, timeToLive);

        var ttl = await Database.KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.True(ttl.Value > TimeSpan.FromSeconds(2), $"The second write left {ttl.Value}; it must restart the {timeToLive} expiry.");
    }

    [Theory]
    [MemberData(nameof(WriteOperations))]
    public async Task Write_WithTimeToLive_KeyExpires(string operation)
    {
        var key = RedisFixture.NewKey("ttl-expires-" + operation);

        await WriteAsync(operation, key, TimeSpan.FromMilliseconds(300));
        await Task.Delay(TimeSpan.FromMilliseconds(800));

        Assert.False(await Database.KeyExistsAsync(key));
    }

    [Fact]
    public async Task IncrementFieldAsync_WithTimeToLive_ReturnsTheNewValue()
    {
        var key = RedisFixture.NewKey("increment-ttl");

        Assert.Equal(5, await HashService.IncrementFieldAsync(key, "count", 5, TimeSpan.FromMinutes(1)));
        Assert.Equal(3, await HashService.IncrementFieldAsync(key, "count", -2, TimeSpan.FromMinutes(1)));
        Assert.Equal(CacheLookup<int>.Hit(3), await HashService.GetFieldAsync(key, "count", Int32));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveTimeToLive_ThrowsArgumentOutOfRangeException_AndWritesNothing(int milliseconds)
    {
        var key = RedisFixture.NewKey("bad-ttl");
        var timeToLive = TimeSpan.FromMilliseconds(milliseconds);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => HashService.SetFieldAsync(key, "f", 1, Int32, timeToLive).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => HashService.SetFieldsAsync(key, new Dictionary<string, int> { ["f"] = 1 }, Int32, timeToLive).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => HashService.IncrementFieldAsync(key, "f", 1, timeToLive).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => HashService.ExpireAsync(key, timeToLive).AsTask());

        Assert.False(await Database.KeyExistsAsync(key));
    }

    // -------------------------------------------------------------------------
    // ExpireAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExpireAsync_ExistingKey_SetsExpiry_AndReturnsTrue()
    {
        var key = RedisFixture.NewKey("expire");
        await HashService.SetFieldAsync(key, "f", 1, Int32);

        Assert.True(await HashService.ExpireAsync(key, TimeSpan.FromSeconds(30)));

        var ttl = await Database.KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task ExpireAsync_Null_RemovesTheExpiry()
    {
        var key = RedisFixture.NewKey("persist");
        await HashService.SetFieldAsync(key, "f", 1, Int32, TimeSpan.FromSeconds(30));

        Assert.True(await HashService.ExpireAsync(key, null));

        Assert.Null(await Database.KeyTimeToLiveAsync(key));
        Assert.True(await Database.KeyExistsAsync(key));

        // Nothing left to remove.
        Assert.False(await HashService.ExpireAsync(key, null));
    }

    [Fact]
    public async Task ExpireAsync_MissingKey_ReturnsFalse_AndCreatesNothing()
    {
        var key = RedisFixture.NewKey("expire-missing");

        Assert.False(await HashService.ExpireAsync(key, TimeSpan.FromSeconds(30)));
        Assert.False(await HashService.ExpireAsync(key, null));
        Assert.False(await Database.KeyExistsAsync(key));
    }

    // -------------------------------------------------------------------------
    // Deletes
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteFieldAsync_ReturnsWhetherTheFieldExisted()
    {
        var key = RedisFixture.NewKey("delete-field");
        await HashService.SetFieldsAsync(key, new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }, Int32);

        Assert.True(await HashService.DeleteFieldAsync(key, "a"));
        Assert.False(await HashService.DeleteFieldAsync(key, "a"));
        Assert.False(await HashService.DeleteFieldAsync(RedisFixture.NewKey("delete-field-missing"), "a"));

        Assert.False((await HashService.GetFieldAsync(key, "a", Int32)).IsHit);
        Assert.Equal(CacheLookup<int>.Hit(2), await HashService.GetFieldAsync(key, "b", Int32));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheHash_AndReturnsWhetherItExisted()
    {
        var key = RedisFixture.NewKey("delete");
        await HashService.SetFieldsAsync(key, new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 }, Int32);

        Assert.True(await HashService.DeleteAsync(key));
        Assert.False(await Database.KeyExistsAsync(key));
        Assert.False(await HashService.DeleteAsync(key));
    }

    // -------------------------------------------------------------------------
    // IncrementFieldAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IncrementFieldAsync_CreatesAtZero_Accumulates_AndDecrements()
    {
        var key = RedisFixture.NewKey("increment");

        Assert.Equal(1, await HashService.IncrementFieldAsync(key, "counter"));
        Assert.Equal(11, await HashService.IncrementFieldAsync(key, "counter", 10));
        Assert.Equal(7, await HashService.IncrementFieldAsync(key, "counter", -4));
    }

    [Fact]
    public async Task IncrementFieldAsync_NonIntegerField_ThrowsRedisServerException()
    {
        var key = RedisFixture.NewKey("increment-non-integer");
        await HashService.SetFieldAsync(key, "name", new TestPayload("A", 1), Payload);

        await Assert.ThrowsAsync<RedisServerException>(() => HashService.IncrementFieldAsync(key, "name").AsTask());
        await Assert.ThrowsAsync<RedisServerException>(() => HashService.IncrementFieldAsync(key, "name", 1, TimeSpan.FromMinutes(1)).AsTask());
    }

    // -------------------------------------------------------------------------
    // Arguments and cancellation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankKey_ThrowsArgumentException(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.GetFieldAsync(key, "f", Int32).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.GetFieldsAsync(key, ["f"], Int32).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.GetAllFieldsAsync(key, Int32).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.SetFieldAsync(key, "f", 1, Int32).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.SetFieldsAsync(key, new Dictionary<string, int> { ["f"] = 1 }, Int32).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.IncrementFieldAsync(key, "f").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.DeleteFieldAsync(key, "f").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.DeleteAsync(key).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.ExpireAsync(key, null).AsTask());
    }

    [Fact]
    public async Task EmptyFieldName_ThrowsArgumentException()
    {
        var key = RedisFixture.NewKey("empty-field");

        await Assert.ThrowsAsync<ArgumentException>(() => HashService.GetFieldAsync(key, "", Int32).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.GetFieldsAsync(key, ["a", ""], Int32).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.SetFieldAsync(key, "", 1, Int32).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.SetFieldsAsync(key, new Dictionary<string, int> { [""] = 1 }, Int32).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.IncrementFieldAsync(key, "").AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => HashService.DeleteFieldAsync(key, "").AsTask());
        Assert.False(await Database.KeyExistsAsync(key));
    }

    [Fact]
    public async Task NullTypeInfoOrValues_ThrowsArgumentNullException()
    {
        var key = RedisFixture.NewKey("null-arguments");

        await Assert.ThrowsAsync<ArgumentNullException>(() => HashService.GetFieldAsync<int>(key, "f", null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => HashService.GetFieldsAsync(key, null!, Int32).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => HashService.SetFieldAsync(key, "f", 1, null!).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => HashService.SetFieldsAsync<int>(key, null!, Int32).AsTask());
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceledException_AndWritesNothing()
    {
        var key = RedisFixture.NewKey("cancelled");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HashService.SetFieldAsync(key, "f", 1, Int32, TimeSpan.FromMinutes(1), cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HashService.SetFieldsAsync(key, new Dictionary<string, int> { ["f"] = 1 }, Int32, null, cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HashService.IncrementFieldAsync(key, "f", 1, null, cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HashService.GetFieldAsync(key, "f", Int32, cts.Token).AsTask());

        Assert.False(await Database.KeyExistsAsync(key));
    }

    // -------------------------------------------------------------------------
    // Failures surface
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetFieldAsync_KeyHoldsAnotherRedisType_Throws()
    {
        var key = RedisFixture.NewKey("wrong-redis-type");
        await Database.StringSetAsync(key, "plain string");

        await Assert.ThrowsAsync<RedisServerException>(() => HashService.SetFieldAsync(key, "f", 1, Int32).AsTask());
    }

    // Regression: TTL writes once ran in MULTI/EXEC, where a failed HSET was swallowed and EXPIRE still applied.
    [Fact]
    public async Task SetFieldAsync_WithTimeToLive_KeyHoldsAnotherRedisType_ThrowsAndLeavesTheKeyUntouched()
    {
        var key = RedisFixture.NewKey("wrong-redis-type-ttl");
        await Database.StringSetAsync(key, "plain string");

        await Assert.ThrowsAsync<RedisServerException>(
            () => HashService.SetFieldAsync(key, "f", 1, Int32, TimeSpan.FromMinutes(1)).AsTask());
        await Assert.ThrowsAsync<RedisServerException>(
            () => HashService.SetFieldsAsync(key, new Dictionary<string, int> { ["f"] = 1 }, Int32, TimeSpan.FromMinutes(1)).AsTask());
        await Assert.ThrowsAsync<RedisServerException>(
            () => HashService.IncrementFieldAsync(key, "f", 1, TimeSpan.FromMinutes(1)).AsTask());

        Assert.Null(await Database.KeyTimeToLiveAsync(key));
    }

    private ValueTask WriteAsync(string operation, string key, TimeSpan? timeToLive) => operation switch
    {
        "SetField" => HashService.SetFieldAsync(key, "f", 1, Int32, timeToLive),
        "SetFields" => HashService.SetFieldsAsync(key, new Dictionary<string, int> { ["f"] = 1, ["g"] = 2 }, Int32, timeToLive),
        "IncrementField" => new ValueTask(HashService.IncrementFieldAsync(key, "f", 1, timeToLive).AsTask()),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
}
