using System.Text;
using System.Text.Json;
using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Contracts.Tests;

public sealed class PageCursorTests
{
    [Fact]
    public void RoundTrips_CommonKeyTypes()
    {
        var at = new DateTimeOffset(2026, 9, 15, 12, 30, 45, TimeSpan.FromHours(3)).AddTicks(1234567);
        var id = Guid.CreateVersion7();

        Decode<DateTimeOffset, Guid>(PageCursor.Encode(at, id)).Should().Be(new CursorPosition<DateTimeOffset, Guid>(at, id));
        Decode<long, int>(PageCursor.Encode(long.MaxValue, 42)).Should().Be(new CursorPosition<long, int>(long.MaxValue, 42));
        Decode<decimal, string>(PageCursor.Encode(59.97m, "ord-1")).Should().Be(new CursorPosition<decimal, string>(59.97m, "ord-1"));
        Decode<DateOnly, Guid>(PageCursor.Encode(new DateOnly(2026, 1, 31), id)).Key.Should().Be(new DateOnly(2026, 1, 31));
    }

    [Fact]
    public void Encode_IsUrlSafeAndVersioned()
    {
        var cursor = PageCursor.Encode("key/with+chars?&=", Guid.Empty);

        cursor.Should().StartWith("v1.");
        cursor.Should().MatchRegex("^v1\\.[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void Encode_RejectsNullAndOversizedKeys()
    {
        FluentActions.Invoking(() => PageCursor.Encode<string, Guid>(null!, Guid.Empty)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => PageCursor.Encode<string, string>("k", null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => PageCursor.Encode(new string('k', 400), 1)).Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("v2.WzEsMl0")]
    [InlineData("v1.!!!")]
    [InlineData("v1.")]
    public void Decode_MalformedInput_IsAFailure(string? cursor) => AssertInvalid<long, long>(cursor);

    [Fact]
    public void Decode_WrongShapeOrTypes_IsAFailure()
    {
        AssertInvalid<long, long>(Raw("{\"a\":1}"));
        AssertInvalid<long, long>(Raw("[1]"));
        AssertInvalid<long, long>(Raw("[1,2,3]"));
        AssertInvalid<long, long>(Raw("[1,2] [3]"));
        AssertInvalid<long, long>(Raw("[\"x\",2]"));
        AssertInvalid<string, long>(Raw("[null,2]"));
        AssertInvalid<long, Guid>(PageCursor.Encode(1L, 2L));
        AssertInvalid<long, long>("v1." + new string('A', PageCursor.MaxLength));
    }

    [Fact]
    public void Decode_UsesTheSuppliedOptions()
    {
        var options = new JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.WriteAsString
                | System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        };

        var cursor = PageCursor.Encode(5L, 6L, options);

        Decode<long, long>(cursor, options).Should().Be(new CursorPosition<long, long>(5, 6));
        AssertInvalid<long, long>(cursor);
    }

    private static CursorPosition<TKey, TId> Decode<TKey, TId>(string cursor, JsonSerializerOptions? options = null)
    {
        var result = PageCursor.Decode<TKey, TId>(cursor, options);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static void AssertInvalid<TKey, TId>(string? cursor)
    {
        var result = PageCursor.Decode<TKey, TId>(cursor);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(PaginationErrorCodes.CursorInvalid);
    }

    private static string Raw(string json) => "v1." + System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
}
