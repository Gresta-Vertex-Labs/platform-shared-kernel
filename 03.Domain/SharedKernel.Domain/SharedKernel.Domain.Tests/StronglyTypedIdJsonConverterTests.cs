using System.Text.Json;
using FluentAssertions;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Domain.StronglyTypedIds.Serialization;
using SharedKernel.Domain.ValueObjects;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Domain.Tests;

public class StronglyTypedIdJsonConverterTests
{
    // --- Test doubles: one concrete StronglyTypedId<TValue> per supported TValue shape ---

    private sealed record GuidId(Guid Value) : StronglyTypedId<Guid>(Value);

    private sealed record IntId(int Value) : StronglyTypedId<int>(Value);

    private sealed record LongId(long Value) : StronglyTypedId<long>(Value);

    private sealed record StringId(string Value) : StronglyTypedId<string>(Value);

    // A plain ValueObject subclass — not a StronglyTypedId<TValue> — used for CanConvert negative cases.
    private sealed class PlainValueObject : ValueObject
    {
        public string Tag { get; }

        public PlainValueObject(string tag) => Tag = tag;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Tag;
        }

        protected override IEnumerable<Error>? Validate() => null;
    }

    private sealed record ContainingDto(GuidId Id, string Name);

    private static JsonSerializerOptions CreateOptionsWithFactory()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new StronglyTypedIdJsonConverterFactory());
        return options;
    }

    // --- (1) Round-trip preserves equality, per supported TValue shape ---

    [Fact]
    public void RoundTrip_GuidId_PreservesEquality()
    {
        var options = CreateOptionsWithFactory();
        var original = new GuidId(Guid.NewGuid());

        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<GuidId>(json, options);

        deserialized.Should().Be(original);
    }

    [Fact]
    public void RoundTrip_IntId_PreservesEquality()
    {
        var options = CreateOptionsWithFactory();
        var original = new IntId(42);

        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<IntId>(json, options);

        deserialized.Should().Be(original);
    }

    [Fact]
    public void RoundTrip_LongId_PreservesEquality()
    {
        var options = CreateOptionsWithFactory();
        var original = new LongId(9_876_543_210L);

        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<LongId>(json, options);

        deserialized.Should().Be(original);
    }

    [Fact]
    public void RoundTrip_StringId_PreservesEquality()
    {
        var options = CreateOptionsWithFactory();
        var original = new StringId("abc");

        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<StringId>(json, options);

        deserialized.Should().Be(original);
    }

    // --- (2) Serialized JSON output is the bare primitive token, not { "value": ... } ---

    [Fact]
    public void Serialize_GuidId_ProducesBareGuidStringToken()
    {
        var options = CreateOptionsWithFactory();
        var guid = Guid.NewGuid();
        var id = new GuidId(guid);

        var json = JsonSerializer.Serialize(id, options);

        json.Should().Be($"\"{guid}\"");
    }

    [Fact]
    public void Serialize_IntId_ProducesBareNumberToken()
    {
        var options = CreateOptionsWithFactory();
        var id = new IntId(42);

        var json = JsonSerializer.Serialize(id, options);

        json.Should().Be("42");
    }

    [Fact]
    public void Serialize_LongId_ProducesBareNumberToken()
    {
        var options = CreateOptionsWithFactory();
        var id = new LongId(9_876_543_210L);

        var json = JsonSerializer.Serialize(id, options);

        json.Should().Be("9876543210");
    }

    [Fact]
    public void Serialize_StringId_ProducesBareStringToken()
    {
        var options = CreateOptionsWithFactory();
        var id = new StringId("abc");

        var json = JsonSerializer.Serialize(id, options);

        json.Should().Be("\"abc\"");
    }

    // --- (3) CanConvert returns false for unrelated types ---

    [Fact]
    public void CanConvert_PlainString_ReturnsFalse()
    {
        var factory = new StronglyTypedIdJsonConverterFactory();

        factory.CanConvert(typeof(string)).Should().BeFalse();
    }

    [Fact]
    public void CanConvert_PlainValueObjectSubclass_ReturnsFalse()
    {
        var factory = new StronglyTypedIdJsonConverterFactory();

        factory.CanConvert(typeof(PlainValueObject)).Should().BeFalse();
    }

    [Fact]
    public void CanConvert_ClosedStronglyTypedId_ReturnsTrue()
    {
        var factory = new StronglyTypedIdJsonConverterFactory();

        factory.CanConvert(typeof(GuidId)).Should().BeTrue();
    }

    // --- (4) Strongly-typed ID property inside a containing DTO round-trips correctly ---

    [Fact]
    public void RoundTrip_ContainingDto_PreservesStronglyTypedIdProperty()
    {
        var options = CreateOptionsWithFactory();
        var dto = new ContainingDto(new GuidId(Guid.NewGuid()), "Widget");

        var json = JsonSerializer.Serialize(dto, options);
        var deserialized = JsonSerializer.Deserialize<ContainingDto>(json, options);

        deserialized.Should().Be(dto);
    }

    // --- (5) Without the factory registered, default STJ record serialization does not match
    //         the bare-primitive expectation — documents the opt-in requirement. ---

    [Fact]
    public void WithoutFactory_Serialize_ProducesObjectWrapperNotBarePrimitive()
    {
        var id = new GuidId(Guid.NewGuid());

        var json = JsonSerializer.Serialize(id);

        // Default STJ record serialization wraps the positional property — not a bare primitive token.
        json.Should().NotBe($"\"{id.Value}\"");
        json.Should().Contain("Value", because: "default record serialization emits an object wrapper with a 'Value' member");
    }

    [Fact]
    public void WithoutFactory_DeserializeFromBarePrimitive_ThrowsJsonException()
    {
        var guid = Guid.NewGuid();
        var bareJson = $"\"{guid}\"";

        var act = () => JsonSerializer.Deserialize<GuidId>(bareJson);

        act.Should().Throw<JsonException>();
    }
}
