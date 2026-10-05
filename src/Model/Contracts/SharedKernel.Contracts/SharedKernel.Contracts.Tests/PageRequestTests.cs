using System.Text.Json;
using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Contracts.Tests;

public sealed class PageRequestTests
{
    [Fact]
    public void Create_Defaults()
    {
        var request = PageRequest.Create().Value;

        request.Page.Should().Be(1);
        request.PageSize.Should().Be(PageRequest.DefaultPageSize);
        request.Offset.Should().Be(0);
        request.Should().Be(PageRequest.First);
    }

    [Fact]
    public void Create_DefaultPageSizeIsCappedByTheEndpointMaximum() =>
        PageRequest.Create(maxPageSize: 5).Value.PageSize.Should().Be(5);

    [Fact]
    public void Offset() => PageRequest.Create(3, 25).Value.Offset.Should().Be(50);

    [Theory]
    [InlineData(0, 10, PaginationErrorCodes.PageOutOfRange)]
    [InlineData(-1, 10, PaginationErrorCodes.PageOutOfRange)]
    [InlineData(1, 0, PaginationErrorCodes.PageSizeOutOfRange)]
    [InlineData(1, 1001, PaginationErrorCodes.PageSizeOutOfRange)]
    [InlineData(int.MaxValue, 1000, PaginationErrorCodes.PageOutOfRange)]
    public void Create_OutOfRange_IsAValidationFailure(int page, int pageSize, string code)
    {
        var result = PageRequest.Create(page, pageSize);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(code);
    }

    [Fact]
    public void Create_ReportsEveryProblem() =>
        PageRequest.Create(0, 0).Errors.Select(e => e.Code)
            .Should().Equal(PaginationErrorCodes.PageOutOfRange, PaginationErrorCodes.PageSizeOutOfRange);

    [Fact]
    public void Create_RespectsTheEndpointMaximum() =>
        PageRequest.Create(1, 51, maxPageSize: 50).Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("between 1 and 50");

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Create_InvalidEndpointMaximum_Throws(int maxPageSize) =>
        FluentActions.Invoking(() => PageRequest.Create(maxPageSize: maxPageSize)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Json_RoundTripsAndRejectsInvalid()
    {
        var request = PageRequest.Create(2, 50).Value;
        var json = JsonSerializer.Serialize(request);

        json.Should().Be("""{"page":2,"pageSize":50}""");
        JsonSerializer.Deserialize<PageRequest>(json).Should().Be(request);
        FluentActions.Invoking(() => JsonSerializer.Deserialize<PageRequest>("""{"page":0,"pageSize":50}"""))
            .Should().Throw<JsonException>();
    }
}
