using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>Test payload used by <see cref="FakeRedisHashServiceTests"/>.</summary>
internal sealed record FakeRedisHashServiceTestDto(string Name, int Value);

[JsonSerializable(typeof(FakeRedisHashServiceTestDto))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(long))]
internal sealed partial class FakeRedisHashServiceTestJsonContext : JsonSerializerContext;

/// <summary>Proves <see cref="FakeRedisHashService"/> against <c>IRedisHashService</c>'s contract.</summary>
public sealed class FakeRedisHashServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static JsonTypeInfo<FakeRedisHashServiceTestDto> DtoTypeInfo =>
        FakeRedisHashServiceTestJsonContext.Default.FakeRedisHashServiceTestDto;

    private static JsonTypeInfo<string> StringTypeInfo => FakeRedisHashServiceTestJsonContext.Default.String;

    private static JsonTypeInfo<long> LongTypeInfo => FakeRedisHashServiceTestJsonContext.Default.Int64;

    [Fact]
    public async Task SetFieldAsync_ThenGetFieldAsync_ReturnsHit_StoredAsJson()
    {
        var service = new FakeRedisHashService();
        var dto = new FakeRedisHashServiceTestDto("Alice", 42);

        await service.SetFieldAsync("hash-1", "field-1", dto, DtoTypeInfo);
        var lookup = await service.GetFieldAsync("hash-1", "field-1", DtoTypeInfo);

        Assert.True(lookup.IsHit);
        Assert.Equal(dto, lookup.Value);
        Assert.Equal(JsonSerializer.Serialize(dto, DtoTypeInfo), service.GetRawField("hash-1", "field-1"));
    }

    [Fact]
    public async Task GetFieldAsync_MissingKeyOrField_ReturnsMiss()
    {
        var service = new FakeRedisHashService();
        await service.SetFieldAsync("hash-1", "field-1", "v", StringTypeInfo);

        Assert.False((await service.GetFieldAsync("hash-1", "other", StringTypeInfo)).IsHit);
        Assert.False((await service.GetFieldAsync("missing", "field-1", StringTypeInfo)).IsHit);
    }

    [Fact]
    public async Task GetFieldAsync_StoredValueOfAnotherShape_ThrowsJsonException()
    {
        var service = new FakeRedisHashService();
        service.SeedRaw("hash-1", "field-1", "not json");

        await Assert.ThrowsAsync<JsonException>(async () => await service.GetFieldAsync("hash-1", "field-1", DtoTypeInfo));
    }

    [Fact]
    public async Task GetFieldsAsync_ReturnsExistingFieldsOnly_AndReadsDuplicatesOnce()
    {
        var service = new FakeRedisHashService();
        await service.SetFieldsAsync("hash-1", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }, StringTypeInfo);

        var result = await service.GetFieldsAsync("hash-1", ["a", "a", "missing"], StringTypeInfo);

        Assert.Equal(new Dictionary<string, string> { ["a"] = "1" }, result);
        Assert.Empty(await service.GetFieldsAsync("hash-1", [], StringTypeInfo));
        Assert.Empty(await service.GetFieldsAsync("missing", ["a"], StringTypeInfo));
    }

    [Fact]
    public async Task GetAllFieldsAsync_ReturnsEveryField_OfThatKeyOnly()
    {
        var service = new FakeRedisHashService();
        await service.SetFieldsAsync("hash-1", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }, StringTypeInfo);
        await service.SetFieldAsync("hash-2", "c", "3", StringTypeInfo);

        var all = await service.GetAllFieldsAsync("hash-1", StringTypeInfo);

        Assert.Equal(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }, all);
        Assert.Empty(await service.GetAllFieldsAsync("missing", StringTypeInfo));
    }

    [Fact]
    public async Task SetFieldsAsync_EmptyValues_ThrowsArgumentException()
    {
        var service = new FakeRedisHashService();

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SetFieldsAsync("hash-1", new Dictionary<string, string>(), StringTypeInfo));
    }

    [Fact]
    public async Task IncrementFieldAsync_MissingField_StartsAtZero_AndReadsBackAsLong()
    {
        var service = new FakeRedisHashService();

        Assert.Equal(5, await service.IncrementFieldAsync("counters", "hits", 5));
        Assert.Equal(3, await service.IncrementFieldAsync("counters", "hits", -2));
        Assert.Equal(4, await service.IncrementFieldAsync("counters", "hits"));

        var lookup = await service.GetFieldAsync("counters", "hits", LongTypeInfo);
        Assert.Equal(4L, lookup.Value);
    }

    [Fact]
    public async Task IncrementFieldAsync_FieldWrittenAsLong_Increments()
    {
        var service = new FakeRedisHashService();
        await service.SetFieldAsync("counters", "hits", 10L, LongTypeInfo);

        Assert.Equal(11, await service.IncrementFieldAsync("counters", "hits"));
    }

    [Fact]
    public async Task IncrementFieldAsync_NonIntegerField_ThrowsInvalidOperation_AndLeavesValue()
    {
        var service = new FakeRedisHashService();
        await service.SetFieldAsync("hash-1", "name", "Alice", StringTypeInfo);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.IncrementFieldAsync("hash-1", "name"));
        Assert.Equal("Alice", (await service.GetFieldAsync("hash-1", "name", StringTypeInfo)).Value);
    }

    [Fact]
    public async Task IncrementFieldAsync_Overflow_ThrowsInvalidOperation()
    {
        var service = new FakeRedisHashService();
        await service.SetFieldAsync("counters", "hits", long.MaxValue, LongTypeInfo);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.IncrementFieldAsync("counters", "hits"));
    }

    [Fact]
    public async Task DeleteFieldAsync_ReturnsWhetherFieldExisted_AndRemovesEmptyHash()
    {
        var service = new FakeRedisHashService();
        await service.SetFieldAsync("hash-1", "field-1", "v", StringTypeInfo);

        Assert.True(await service.DeleteFieldAsync("hash-1", "field-1"));
        Assert.False(await service.DeleteFieldAsync("hash-1", "field-1"));
        Assert.False(service.ContainsKey("hash-1"));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsWhetherKeyExisted()
    {
        var service = new FakeRedisHashService();
        await service.SetFieldAsync("hash-1", "field-1", "v", StringTypeInfo);

        Assert.True(await service.DeleteAsync("hash-1"));
        Assert.False(await service.DeleteAsync("hash-1"));
        Assert.Empty(await service.GetAllFieldsAsync("hash-1", StringTypeInfo));
    }

    [Fact]
    public async Task WriteWithTimeToLive_ExpiresWholeHash()
    {
        var time = new ManualTimeProvider(Start);
        var service = new FakeRedisHashService(time);

        await service.SetFieldAsync("session", "a", "1", StringTypeInfo);
        await service.SetFieldAsync("session", "b", "2", StringTypeInfo, TimeSpan.FromMinutes(30));
        Assert.Equal(TimeSpan.FromMinutes(30), service.GetTimeToLive("session"));

        time.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(2, (await service.GetAllFieldsAsync("session", StringTypeInfo)).Count);

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.False((await service.GetFieldAsync("session", "a", StringTypeInfo)).IsHit);
        Assert.False(service.ContainsKey("session"));
        Assert.Empty(service.Keys);
    }

    [Fact]
    public async Task WriteWithTimeToLive_RestartsExpiry_WriteWithoutKeepsIt()
    {
        var time = new ManualTimeProvider(Start);
        var service = new FakeRedisHashService(time);

        await service.SetFieldAsync("session", "a", "1", StringTypeInfo, TimeSpan.FromMinutes(10));
        time.Advance(TimeSpan.FromMinutes(5));
        await service.IncrementFieldAsync("session", "hits", timeToLive: TimeSpan.FromMinutes(10));
        time.Advance(TimeSpan.FromMinutes(2));
        await service.SetFieldAsync("session", "b", "2", StringTypeInfo);

        Assert.Equal(TimeSpan.FromMinutes(8), service.GetTimeToLive("session"));
    }

    [Fact]
    public async Task ExpiredKey_WrittenAgain_StartsEmpty()
    {
        var time = new ManualTimeProvider(Start);
        var service = new FakeRedisHashService(time);
        await service.SetFieldAsync("session", "old", "1", StringTypeInfo, TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(1));

        await service.SetFieldAsync("session", "new", "2", StringTypeInfo);

        Assert.Equal(["new"], (await service.GetAllFieldsAsync("session", StringTypeInfo)).Keys);
        Assert.Null(service.GetTimeToLive("session"));
    }

    [Fact]
    public async Task ExpireAsync_SetsAndPersists_ReturningWhetherExpiryChanged()
    {
        var time = new ManualTimeProvider(Start);
        var service = new FakeRedisHashService(time);

        Assert.False(await service.ExpireAsync("missing", TimeSpan.FromMinutes(1)));

        await service.SetFieldAsync("hash-1", "a", "1", StringTypeInfo);
        Assert.False(await service.ExpireAsync("hash-1", null));
        Assert.True(await service.ExpireAsync("hash-1", TimeSpan.FromMinutes(1)));
        Assert.Equal(TimeSpan.FromMinutes(1), service.GetTimeToLive("hash-1"));
        Assert.True(await service.ExpireAsync("hash-1", null));
        Assert.Null(service.GetTimeToLive("hash-1"));

        time.Advance(TimeSpan.FromHours(1));
        Assert.True(service.ContainsKey("hash-1"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveTimeToLive_ThrowsArgumentOutOfRange(int seconds)
    {
        var service = new FakeRedisHashService();
        var ttl = TimeSpan.FromSeconds(seconds);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await service.SetFieldAsync("k", "f", "v", StringTypeInfo, ttl));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await service.SetFieldsAsync("k", new Dictionary<string, string> { ["f"] = "v" }, StringTypeInfo, ttl));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await service.IncrementFieldAsync("k", "f", 1, ttl));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await service.ExpireAsync("k", ttl));
        Assert.False(service.ContainsKey("k"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task InvalidKey_Throws(string? key)
    {
        var service = new FakeRedisHashService();

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await service.GetFieldAsync(key!, "f", StringTypeInfo));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await service.SetFieldAsync(key!, "f", "v", StringTypeInfo));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await service.DeleteAsync(key!));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await service.ExpireAsync(key!, null));
    }

    [Fact]
    public async Task EmptyField_Throws_ButWhitespaceFieldIsValid()
    {
        var service = new FakeRedisHashService();

        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await service.SetFieldAsync("k", "", "v", StringTypeInfo));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await service.GetFieldsAsync("k", ["a", ""], StringTypeInfo));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await service.DeleteFieldAsync("k", null!));

        await service.SetFieldAsync("k", " ", "v", StringTypeInfo);
        Assert.True((await service.GetFieldAsync("k", " ", StringTypeInfo)).IsHit);
    }

    [Fact]
    public async Task CancelledToken_Throws_BeforeWriting()
    {
        var service = new FakeRedisHashService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await service.SetFieldAsync("k", "f", "v", StringTypeInfo, ct: cts.Token));

        Assert.False(service.ContainsKey("k"));
    }

    [Fact]
    public async Task SimulateFailure_EveryOperation_ThrowsTimeout()
    {
        var service = new FakeRedisHashService { SimulateFailure = true };

        await Assert.ThrowsAsync<TimeoutException>(async () => await service.GetFieldAsync("k", "f", StringTypeInfo));
        await Assert.ThrowsAsync<TimeoutException>(async () => await service.GetFieldsAsync("k", ["f"], StringTypeInfo));
        await Assert.ThrowsAsync<TimeoutException>(async () => await service.GetAllFieldsAsync("k", StringTypeInfo));
        await Assert.ThrowsAsync<TimeoutException>(async () => await service.SetFieldAsync("k", "f", "v", StringTypeInfo));
        await Assert.ThrowsAsync<TimeoutException>(
            async () => await service.SetFieldsAsync("k", new Dictionary<string, string> { ["f"] = "v" }, StringTypeInfo));
        await Assert.ThrowsAsync<TimeoutException>(async () => await service.IncrementFieldAsync("k", "f"));
        await Assert.ThrowsAsync<TimeoutException>(async () => await service.DeleteFieldAsync("k", "f"));
        await Assert.ThrowsAsync<TimeoutException>(async () => await service.DeleteAsync("k"));
        await Assert.ThrowsAsync<TimeoutException>(async () => await service.ExpireAsync("k", null));
    }

    [Fact]
    public async Task Seed_BypassesSimulateFailure_AndReset_ClearsEverything()
    {
        var service = new FakeRedisHashService { SimulateFailure = true };
        service.Seed("hash-1", "field-1", new FakeRedisHashServiceTestDto("Bob", 7), DtoTypeInfo);
        service.SimulateFailure = false;

        Assert.Equal(new FakeRedisHashServiceTestDto("Bob", 7), (await service.GetFieldAsync("hash-1", "field-1", DtoTypeInfo)).Value);

        service.Reset();

        Assert.Empty(service.Keys);
    }

    internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
