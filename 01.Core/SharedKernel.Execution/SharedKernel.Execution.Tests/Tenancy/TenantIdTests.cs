using System.Text.Json;
using FluentAssertions;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Execution.Tests.Tenancy;

public sealed class TenantIdTests
{
    private static readonly Guid Value = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Fact]
    public void Constructor_RejectsEmptyGuid()
    {
        var act = () => new TenantId(Guid.Empty);

        act.Should().Throw<ArgumentException>().WithParameterName("value");
    }

    [Fact]
    public void Default_IsDefault_ConstructedIsNot()
    {
        default(TenantId).IsDefault.Should().BeTrue();
        new TenantId(Value).IsDefault.Should().BeFalse();
    }

    [Fact]
    public void Equality_IsByValue()
    {
        new TenantId(Value).Should().Be(new TenantId(Value));
        (new TenantId(Value) == new TenantId(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public void FromNullable_TreatsNullAndEmptyAsNoTenant()
    {
        TenantId.FromNullable(null).Should().BeNull();
        TenantId.FromNullable(Guid.Empty).Should().BeNull();
        TenantId.FromNullable(Value).Should().Be(new TenantId(Value));
    }

    [Fact]
    public void ToString_IsLowercaseHyphenated()
    {
        new TenantId(Value).ToString().Should().Be("0f8fad5b-d9cb-469f-a165-70867728950e");
        $"{new TenantId(Value)}".Should().Be("0f8fad5b-d9cb-469f-a165-70867728950e");
    }

    [Fact]
    public void TryFormat_WritesTheSameForm()
    {
        Span<char> buffer = stackalloc char[64];

        new TenantId(Value).TryFormat(buffer, out var written, default, null).Should().BeTrue();

        buffer[..written].ToString().Should().Be(Value.ToString("D"));
    }

    [Theory]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e", true)]
    [InlineData("0F8FAD5B-D9CB-469F-A165-70867728950E", true)]
    [InlineData("00000000-0000-0000-0000-000000000000", false)]
    [InlineData("not-a-guid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_AcceptsOnlyNonEmptyGuids(string? input, bool expected)
    {
        TenantId.TryParse(input, out var result).Should().Be(expected);

        if (expected)
            result.Value.Should().Be(Value);
        else
            result.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void Parse_ThrowsFormatExceptionForEmptyGuid()
    {
        var act = () => TenantId.Parse(Guid.Empty.ToString());

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ExplicitConversions_RoundTrip()
    {
        var tenant = (TenantId)Value;

        ((Guid)tenant).Should().Be(Value);
    }

    [Fact]
    public void Json_RoundTripsAsString()
    {
        var json = JsonSerializer.Serialize(new TenantId(Value));

        json.Should().Be("\"0f8fad5b-d9cb-469f-a165-70867728950e\"");
        JsonSerializer.Deserialize<TenantId>(json).Should().Be(new TenantId(Value));
    }

    [Fact]
    public void Json_NullableRoundTripsNull()
    {
        JsonSerializer.Serialize<TenantId?>(null).Should().Be("null");
        JsonSerializer.Deserialize<TenantId?>("null").Should().BeNull();
    }

    [Theory]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("\"not-a-guid\"")]
    [InlineData("42")]
    public void Json_RejectsInvalidValues(string json)
    {
        var act = () => JsonSerializer.Deserialize<TenantId>(json);

        act.Should().Throw<JsonException>();
    }
}
