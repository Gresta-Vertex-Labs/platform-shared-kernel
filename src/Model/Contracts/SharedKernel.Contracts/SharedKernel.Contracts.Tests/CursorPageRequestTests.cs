using System.Text.Json;
using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Contracts.Tests;

public sealed class CursorPageRequestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_NoCursorIsTheFirstPage(string? cursor)
    {
        var request = CursorPageRequest.Create(cursor).Value;

        request.IsFirstPage.Should().BeTrue();
        request.Cursor.Should().BeNull();
        request.Limit.Should().Be(CursorPageRequest.DefaultLimit);
        request.Should().Be(CursorPageRequest.First);
    }

    [Fact]
    public void Create_KeepsTheCursor()
    {
        var request = CursorPageRequest.Create("v1.abc", 5).Value;

        request.IsFirstPage.Should().BeFalse();
        request.Cursor.Should().Be("v1.abc");
        request.Limit.Should().Be(5);
    }

    [Theory]
    [InlineData("v1.x", 0, PaginationErrorCodes.LimitOutOfRange)]
    [InlineData("v1.x", 1001, PaginationErrorCodes.LimitOutOfRange)]
    [InlineData("   ", 10, PaginationErrorCodes.CursorInvalid)]
    public void Create_Invalid_IsAValidationFailure(string cursor, int limit, string code) =>
        CursorPageRequest.Create(cursor, limit).Errors.Should().ContainSingle().Which.Code.Should().Be(code);

    [Fact]
    public void Create_TooLongCursor_IsAValidationFailure() =>
        CursorPageRequest.Create(new string('a', PageCursor.MaxLength + 1)).Errors.Should().ContainSingle()
            .Which.Code.Should().Be(PaginationErrorCodes.CursorInvalid);

    [Fact]
    public void Create_DefaultLimitIsCappedByTheEndpointMaximum() =>
        CursorPageRequest.Create(maxLimit: 3).Value.Limit.Should().Be(3);

    [Fact]
    public void Json_RoundTrips()
    {
        var request = CursorPageRequest.Create("v1.abc", 5).Value;
        var json = JsonSerializer.Serialize(request);

        json.Should().Be("""{"cursor":"v1.abc","limit":5}""");
        JsonSerializer.Deserialize<CursorPageRequest>(json).Should().Be(request);
        JsonSerializer.Serialize(CursorPageRequest.First).Should().Be("""{"limit":20}""");
    }
}
