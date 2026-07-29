using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>Test payload used by <see cref="FakeRedisHashServiceTests"/>.</summary>
internal sealed record FakeRedisHashServiceTestDto(string Name, int Value);

[JsonSerializable(typeof(FakeRedisHashServiceTestDto))]
[JsonSerializable(typeof(string))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal sealed partial class FakeRedisHashServiceTestJsonContext : JsonSerializerContext;

/// <summary>
/// Proves <see cref="FakeRedisHashService"/> against <c>IRedisHashService</c>'s contract — no
/// existing `02.Caching` consuming-domain test (e.g. `SharedKernel.Caching.Redis.HashStore.Tests`)
/// currently exercises this fake directly, so coverage is provided here per the documented SelfTests
/// fallback.
/// </summary>
public sealed class FakeRedisHashServiceTests
{
    private static JsonTypeInfo<FakeRedisHashServiceTestDto> DtoTypeInfo =>
        FakeRedisHashServiceTestJsonContext.Default.FakeRedisHashServiceTestDto;

    private static JsonTypeInfo<string> StringTypeInfo => FakeRedisHashServiceTestJsonContext.Default.String;

    [Fact]
    public async Task SetFieldAsync_ThenGetFieldAsync_RoundTrips()
    {
        var service = new FakeRedisHashService();
        var dto = new FakeRedisHashServiceTestDto("Alice", 42);

        await service.SetFieldAsync("hash-1", "field-1", dto, DtoTypeInfo);
        var result = await service.GetFieldAsync("hash-1", "field-1", DtoTypeInfo);

        Assert.Equal(dto, result);
    }

    [Fact]
    public async Task GetFieldAsync_MissingField_ReturnsDefault()
    {
        var service = new FakeRedisHashService();

        var result = await service.GetFieldAsync("hash-1", "missing", DtoTypeInfo);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllFieldsAsync_ReturnsOnlyFieldsOfRequestedType_TypeFiltered()
    {
        var service = new FakeRedisHashService();
        var dto = new FakeRedisHashServiceTestDto("Bob", 7);
        await service.SetFieldAsync("hash-1", "dto-field", dto, DtoTypeInfo);
        await service.SetFieldAsync("hash-1", "string-field", "not-a-dto", StringTypeInfo);

        var result = await service.GetAllFieldsAsync("hash-1", DtoTypeInfo);

        Assert.Single(result);
        Assert.Equal(dto, result["dto-field"]);
    }

    [Fact]
    public async Task GetAllFieldsAsync_ExcludesFieldsUnderOtherKeys()
    {
        var service = new FakeRedisHashService();
        await service.SetFieldAsync("hash-1", "field-1", "value-1", StringTypeInfo);
        await service.SetFieldAsync("hash-2", "field-1", "value-2", StringTypeInfo);

        var result = await service.GetAllFieldsAsync("hash-1", StringTypeInfo);

        Assert.Single(result);
        Assert.Equal("value-1", result["field-1"]);
    }

    [Fact]
    public async Task DeleteFieldAsync_RemovesField()
    {
        var service = new FakeRedisHashService();
        await service.SetFieldAsync("hash-1", "field-1", "value", StringTypeInfo);

        await service.DeleteFieldAsync("hash-1", "field-1");

        Assert.Null(await service.GetFieldAsync("hash-1", "field-1", StringTypeInfo));
    }

    [Fact]
    public async Task DeleteFieldAsync_MissingField_IsIdempotentNoOp()
    {
        var service = new FakeRedisHashService();

        var exception = await Record.ExceptionAsync(async () => await service.DeleteFieldAsync("hash-1", "missing"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task IncrementFieldAsync_MissingField_TreatedAsZero()
    {
        var service = new FakeRedisHashService();

        var result = await service.IncrementFieldAsync("hash-1", "counter", 5);

        Assert.Equal(5, result);
    }

    [Fact]
    public async Task IncrementFieldAsync_ExistingField_AddsDelta()
    {
        var service = new FakeRedisHashService();
        await service.IncrementFieldAsync("hash-1", "counter", 5);

        var result = await service.IncrementFieldAsync("hash-1", "counter", 3);

        Assert.Equal(8, result);
    }

    [Fact]
    public async Task IncrementFieldAsync_NonLongField_ThrowsInvalidCastException()
    {
        var service = new FakeRedisHashService();
        service.Seed("hash-1", "field-1", "not-a-long");

        await Assert.ThrowsAsync<InvalidCastException>(async () => await service.IncrementFieldAsync("hash-1", "field-1", 1));
    }

    [Fact]
    public async Task Seed_PrePopulatesField_WithoutGoingThroughSetFieldAsync()
    {
        var service = new FakeRedisHashService();
        service.Seed("hash-1", "field-1", "seeded-value");

        var result = await service.GetFieldAsync("hash-1", "field-1", StringTypeInfo);

        Assert.Equal("seeded-value", result);
    }

    [Fact]
    public async Task Reset_ClearsAllStoredFields()
    {
        var service = new FakeRedisHashService();
        await service.SetFieldAsync("hash-1", "field-1", "value", StringTypeInfo);

        service.Reset();

        Assert.Null(await service.GetFieldAsync("hash-1", "field-1", StringTypeInfo));
    }

    [Fact]
    public async Task SimulateFailure_GetFieldAsync_Throws()
    {
        var service = new FakeRedisHashService { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.GetFieldAsync("hash-1", "field-1", StringTypeInfo));
    }

    [Fact]
    public async Task SimulateFailure_SetFieldAsync_Throws()
    {
        var service = new FakeRedisHashService { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.SetFieldAsync("hash-1", "field-1", "value", StringTypeInfo));
    }

    [Fact]
    public async Task SimulateFailure_GetAllFieldsAsync_Throws()
    {
        var service = new FakeRedisHashService { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.GetAllFieldsAsync("hash-1", StringTypeInfo));
    }

    [Fact]
    public async Task SimulateFailure_DeleteFieldAsync_Throws()
    {
        var service = new FakeRedisHashService { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.DeleteFieldAsync("hash-1", "field-1"));
    }

    [Fact]
    public async Task SimulateFailure_IncrementFieldAsync_Throws()
    {
        var service = new FakeRedisHashService { SimulateFailure = true };

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.IncrementFieldAsync("hash-1", "field-1", 1));
    }
}
