using FluentAssertions;
using SharedKernel.Persistence.Abstractions.Repositories;

namespace SharedKernel.Persistence.Abstractions.Tests.Repositories;

/// <summary>F13: the opaque, ETag-friendly version type that replaced <c>uint</c> in the repository contract.</summary>
public sealed class EntityVersionTests
{
    [Theory]
    [InlineData("42")]
    [InlineData("\"42\"")]
    [InlineData("W/\"42\"")]
    [InlineData("  42 ")]
    public void TryParse_AcceptsTheTextForm_QuotedOrWeak(string text)
    {
        EntityVersion.TryParse(text, out var version).Should().BeTrue();
        version.Should().Be(EntityVersion.FromRowVersion(42));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"\"")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("abc")]
    [InlineData("4.2")]
    public void TryParse_RejectsAnythingElse(string? text) =>
        EntityVersion.TryParse(text, out _).Should().BeFalse();

    [Fact]
    public void ToString_RoundTripsThroughAnETag()
    {
        var version = EntityVersion.FromRowVersion(uint.MaxValue);
        var etag = $"\"{version}\"";

        EntityVersion.Parse(etag).Should().Be(version);
        version.ToString().Should().Be("4294967295");
        $"{version}".Should().Be(version.ToString());
    }

    [Fact]
    public void None_IsTheDefault_AndEqualityIsByValue()
    {
        EntityVersion.None.Should().Be(default(EntityVersion));
        (EntityVersion.FromRowVersion(7) == EntityVersion.FromRowVersion(7)).Should().BeTrue();
        (EntityVersion.FromRowVersion(7) != EntityVersion.FromRowVersion(8)).Should().BeTrue();
        EntityVersion.FromRowVersion(7).GetHashCode().Should().Be(EntityVersion.FromRowVersion(7).GetHashCode());
    }

    [Fact]
    public void Parse_Invalid_ThrowsFormatException() =>
        FluentActions.Invoking(() => EntityVersion.Parse("nope")).Should().Throw<FormatException>();
}
