using System.Buffers.Text;
using System.Text.Json;
using FluentAssertions;
using SharedKernel.Persistence.Abstractions.Repositories;

namespace SharedKernel.Persistence.Abstractions.Tests.Repositories;

/// <summary>
/// F13 and P-562 X4: the opaque version type of the repository contract. It holds only a sealed token (the EfCore
/// codec's output); these tests pin its text form, which is also what makes a raw row version impossible to put on the
/// wire through it.
/// </summary>
public sealed class EntityVersionTests
{
    // A well-formed token: format byte 0x01 followed by 20 provider bytes, as unpadded Base64Url (28 characters).
    private static string Token(byte fill, byte format = 0x01)
    {
        var bytes = new byte[21];
        Array.Fill(bytes, fill);
        bytes[0] = format;
        return Base64Url.EncodeToString(bytes);
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("\"{0}\"")]
    [InlineData("W/\"{0}\"")]
    [InlineData("  {0} ")]
    public void TryParse_AcceptsTheTokenText_QuotedOrWeak(string pattern)
    {
        var token = Token(0x5A);

        EntityVersion.TryParse(string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, token), out var version)
            .Should().BeTrue();
        version.ToString().Should().Be(token);
    }

    [Fact]
    public void ToString_RoundTripsThroughAnETag()
    {
        var version = EntityVersion.Parse(Token(0xC3));
        var etag = $"\"{version}\"";

        EntityVersion.Parse(etag).Should().Be(version);
        version.ToString().Should().HaveLength(28);
        $"{version}".Should().Be(version.ToString());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("42")]
    [InlineData("\"42\"")]
    [InlineData("W/\"42\"")]
    [InlineData("4294967295")]
    [InlineData("18446744073709551615")]
    [InlineData("1234567890123456789012345678")] // 28 digits: Base64Url-shaped, but not the version-1 format
    public void X4_ARawRowVersion_IsNeverAVersion(string text) =>
        EntityVersion.TryParse(text, out _).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"\"")]
    [InlineData("abc")]
    [InlineData("-1")]
    public void TryParse_RejectsAnythingElse(string? text) =>
        EntityVersion.TryParse(text, out _).Should().BeFalse();

    [Fact]
    public void TryParse_RejectsTheWrongLength_TheWrongAlphabet_AndAnotherFormat()
    {
        var token = Token(0x11);

        EntityVersion.TryParse(token[..27], out _).Should().BeFalse("27 characters");
        EntityVersion.TryParse(token + "A", out _).Should().BeFalse("29 characters");
        EntityVersion.TryParse(token[..26] + "==", out _).Should().BeFalse("padding");
        EntityVersion.TryParse(token[..13] + "+" + token[14..], out _).Should().BeFalse("'+' is Base64, not Base64Url");
        EntityVersion.TryParse(token[..13] + " " + token[14..], out _).Should().BeFalse("whitespace inside");
        EntityVersion.TryParse(Token(0x11, format: 0x02), out _).Should().BeFalse("an unknown format");
    }

    [Fact]
    public void None_IsTheDefault_HasNoText_AndNothingParsesToIt()
    {
        EntityVersion.None.Should().Be(default(EntityVersion));
        EntityVersion.None.ToString().Should().BeEmpty();
        EntityVersion.TryParse(EntityVersion.None.ToString(), out _).Should().BeFalse();

        Span<char> buffer = stackalloc char[28];
        EntityVersion.None.TryFormat(buffer, out var written, default, null).Should().BeTrue();
        written.Should().Be(0);
    }

    [Fact]
    public void EqualityIsByToken()
    {
        var a = EntityVersion.Parse(Token(7));

        (a == EntityVersion.Parse(Token(7))).Should().BeTrue();
        (a != EntityVersion.Parse(Token(8))).Should().BeTrue();
        a.GetHashCode().Should().Be(EntityVersion.Parse(Token(7)).GetHashCode());
        a.Should().NotBe(EntityVersion.None);
    }

    [Fact]
    public void TryFormat_IntoATooSmallBuffer_ReportsFailure()
    {
        var version = EntityVersion.Parse(Token(9));
        Span<char> buffer = stackalloc char[27];

        version.TryFormat(buffer, out var written, default, null).Should().BeFalse();
        written.Should().Be(0);
    }

    [Fact]
    public void Parse_Invalid_ThrowsFormatException() =>
        FluentActions.Invoking(() => EntityVersion.Parse("nope")).Should().Throw<FormatException>();

    [Fact]
    public void IsParsable_ForGenericBinders()
    {
        static T ParseAs<T>(string text) where T : IParsable<T> => T.Parse(text, provider: null);

        ParseAs<EntityVersion>(Token(3)).Should().Be(EntityVersion.Parse(Token(3)));
    }

    // ---- JSON: the token or null, never an object and never a number ----

    public sealed record Snapshot(string Name, EntityVersion Version, EntityVersion? Previous);

    [Fact]
    public void X4_Json_WritesTheTokenAsAString_AndNoneAsNull()
    {
        var version = EntityVersion.Parse(Token(0x42));

        var json = JsonSerializer.Serialize(new Snapshot("a", version, null));

        json.Should().Be($$"""{"Name":"a","Version":"{{version}}","Previous":null}""");
        JsonSerializer.Serialize(EntityVersion.None).Should().Be("null");
    }

    [Fact]
    public void X4_Json_RoundTrips()
    {
        var snapshot = new Snapshot("a", EntityVersion.Parse(Token(0x42)), EntityVersion.Parse(Token(0x43)));

        JsonSerializer.Deserialize<Snapshot>(JsonSerializer.Serialize(snapshot)).Should().Be(snapshot);
        JsonSerializer.Deserialize<EntityVersion>("null").Should().Be(EntityVersion.None);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"42\"")]
    [InlineData("{\"value\":42}")]
    [InlineData("[]")]
    [InlineData("\"not-a-version\"")]
    public void X4_Json_RefusesAnythingButAToken(string json) =>
        FluentActions.Invoking(() => JsonSerializer.Deserialize<EntityVersion>(json)).Should().Throw<JsonException>();

    [Fact]
    public void X4_Json_ReadsTheEmptyObjectThePreviousTypeWrote_AsNone()
    {
        // Before X4 the fields were private, so every version serialized as {} — documents stored then still read.
        JsonSerializer.Deserialize<EntityVersion>("{}").Should().Be(EntityVersion.None);
        JsonSerializer.Deserialize<Snapshot>("""{"Name":"a","Version":{},"Previous":{}}""")
            .Should().Be(new Snapshot("a", EntityVersion.None, EntityVersion.None));
    }
}
