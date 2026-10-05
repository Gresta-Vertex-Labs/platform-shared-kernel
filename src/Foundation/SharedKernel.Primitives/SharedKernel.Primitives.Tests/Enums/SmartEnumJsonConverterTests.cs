using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.Primitives.Enums;
using Xunit;

namespace SharedKernel.Primitives.Tests.Enums;

/// <summary>
/// Covers <see cref="SmartEnumJsonConverter{TEnum, TValue}"/>.
/// </summary>
/// <remarks>
/// A <see cref="SmartEnum{TEnum, TValue}"/> is write-only over JSON without this converter: the
/// default object serializer emits both properties and then cannot read them back, because every
/// member is a singleton behind a private constructor. These tests pin the round trip, the wire
/// form (the underlying value, never the name), and the failure mode for an unknown value.
/// </remarks>
public sealed class SmartEnumJsonConverterTests
{
    private static JsonSerializerOptions IntOptions() =>
        new() { Converters = { new SmartEnumJsonConverter<JsonStatus, int>() } };

    private static JsonSerializerOptions StringOptions() =>
        new() { Converters = { new SmartEnumJsonConverter<JsonCurrency, string>() } };

    [Fact]
    public void Write_EmitsTheUnderlyingValue_NotAnObject()
    {
        var json = JsonSerializer.Serialize(JsonStatus.Shipped, IntOptions());

        Assert.Equal("2", json);
    }

    [Fact]
    public void Read_ResolvesTheDeclaredMemberInstance()
    {
        var status = JsonSerializer.Deserialize<JsonStatus>("2", IntOptions());

        // Same instance, not an equal copy — members are singletons and must stay so, or
        // reference equality (which SmartEnum relies on) would silently break after a round trip.
        Assert.Same(JsonStatus.Shipped, status);
    }

    [Fact]
    public void RoundTrip_PreservesTheMember()
    {
        var options = IntOptions();

        var json = JsonSerializer.Serialize(JsonStatus.Pending, options);
        var restored = JsonSerializer.Deserialize<JsonStatus>(json, options);

        Assert.Same(JsonStatus.Pending, restored);
    }

    [Fact]
    public void RoundTrip_WorksForAStringBackedEnum()
    {
        var options = StringOptions();

        var json = JsonSerializer.Serialize(JsonCurrency.Euro, options);
        var restored = JsonSerializer.Deserialize<JsonCurrency>(json, options);

        Assert.Equal("\"EUR\"", json);
        Assert.Same(JsonCurrency.Euro, restored);
    }

    [Fact]
    public void Read_UnknownValue_ThrowsJsonExceptionNamingTypeAndValue()
    {
        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<JsonStatus>("99", IntOptions())
        );

        Assert.Contains("99", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(JsonStatus), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_Null_YieldsNull()
    {
        var status = JsonSerializer.Deserialize<JsonStatus?>("null", IntOptions());

        Assert.Null(status);
    }

    [Fact]
    public void Write_Null_EmitsJsonNull()
    {
        var json = JsonSerializer.Serialize<JsonStatus?>(null, IntOptions());

        Assert.Equal("null", json);
    }

    [Fact]
    public void PropertyLevelAttribute_AppliesWithoutOptionsRegistration()
    {
        // The per-property form documented in the README, which needs no options plumbing at all.
        var json = JsonSerializer.Serialize(new OrderDto(JsonStatus.Shipped));

        Assert.Equal("""{"Status":2}""", json);

        var restored = JsonSerializer.Deserialize<OrderDto>(json);

        Assert.Same(JsonStatus.Shipped, restored!.Status);
    }

    [Fact]
    public void WithoutTheConverter_TheDefaultSerializerCannotRoundTrip()
    {
        // Documents WHY this converter exists. The default serializer writes both properties and
        // then cannot construct the type back, because the constructor is private.
        var json = JsonSerializer.Serialize(JsonStatus.Shipped);

        Assert.Contains("\"Name\"", json, StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<JsonStatus>(json));
    }

    // ---- Fixtures ----

    private sealed record OrderDto(
        [property: JsonConverter(typeof(SmartEnumJsonConverter<JsonStatus, int>))]
            JsonStatus Status
    );

    private sealed class JsonStatus : SmartEnum<JsonStatus, int>
    {
        public static readonly JsonStatus Pending = new(nameof(Pending), 1);
        public static readonly JsonStatus Shipped = new(nameof(Shipped), 2);

        private JsonStatus(string name, int value)
            : base(name, value) { }
    }

    private sealed class JsonCurrency : SmartEnum<JsonCurrency, string>
    {
        public static readonly JsonCurrency Euro = new(nameof(Euro), "EUR");
        public static readonly JsonCurrency Lira = new(nameof(Lira), "TRY");

        private JsonCurrency(string name, string value)
            : base(name, value) { }
    }
}
