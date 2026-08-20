using FluentAssertions;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Concurrency;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Concurrency;

public class RowVersionETagTests
{
    public static TheoryData<byte[]> RowVersionCases() => new()
    {
        Array.Empty<byte>(),
        new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, // typical 8-byte SQL rowversion/timestamp token
        Enumerable.Range(0, 64).Select(i => (byte)i).ToArray(), // a larger token
    };

    [Theory]
    [MemberData(nameof(RowVersionCases))]
    public void From_ProducesWellFormedQuotedETag(byte[] rowVersion)
    {
        var etag = RowVersionETag.From(rowVersion);

        etag.Should().StartWith("\"");
        etag.Should().EndWith("\"");
        etag.Should().Be($"\"{Convert.ToBase64String(rowVersion)}\"");
    }

    [Theory]
    [MemberData(nameof(RowVersionCases))]
    public void From_ProducesAValueParseableAsAStrongEntityTag(byte[] rowVersion)
    {
        var etag = RowVersionETag.From(rowVersion);

        var parsed = EntityTagHeaderValue.Parse(etag);

        parsed.IsWeak.Should().BeFalse();
        parsed.Tag.ToString().Should().Be(etag);
    }

    [Fact]
    public void From_NullRowVersion_Throws()
    {
        Action act = () => RowVersionETag.From(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
