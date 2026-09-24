using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// P-563 P4: <see cref="Paging"/> and <see cref="CursorPaging"/> bind the paging query parameters into 04.Contracts'
/// validated requests. Absent parameters take the contract's defaults; invalid input is answered 400 with the
/// platform's validation problem — <c>errors</c>/<c>errorCodes</c> keyed by the query parameter — before the handler
/// runs.
/// </summary>
public sealed class PagingTests : IAsyncLifetime
{
    private WebApplication? _app;
    private int _calls;

    private HttpClient Client => _app!.GetTestClient();

    public async Task InitializeAsync()
    {
        _app = await WebApiTestHost.StartAsync(app =>
        {
            app.MapGet("/items", (Paging paging) =>
            {
                Interlocked.Increment(ref _calls);
                return new PageAnswer(paging.Request.Page, paging.Request.PageSize);
            });
            app.MapGet("/feed", (CursorPaging paging) =>
            {
                Interlocked.Increment(ref _calls);
                return new CursorAnswer(paging.Request.Cursor, paging.Request.Limit);
            });
        });
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Paging_WithoutParameters_UsesTheContractDefaults()
    {
        var answer = await Client.GetFromJsonAsync<PageAnswer>("/items");

        answer.Should().Be(new PageAnswer(1, PageRequest.DefaultPageSize));
    }

    [Theory]
    [InlineData("?page=3&pageSize=50", 3, 50)]
    [InlineData("?page=2", 2, PageRequest.DefaultPageSize)]
    [InlineData("?pageSize=1000", 1, 1000)]
    [InlineData("?page=&pageSize=", 1, PageRequest.DefaultPageSize)]
    public async Task Paging_WithValidParameters_BindsThem(string query, int page, int pageSize)
    {
        var answer = await Client.GetFromJsonAsync<PageAnswer>("/items" + query);

        answer.Should().Be(new PageAnswer(page, pageSize));
    }

    [Theory]
    [InlineData("?page=0", PagingQuery.Page, PaginationErrorCodes.PageOutOfRange)]
    [InlineData("?page=-4", PagingQuery.Page, PaginationErrorCodes.PageOutOfRange)]
    [InlineData("?pageSize=0", PagingQuery.PageSize, PaginationErrorCodes.PageSizeOutOfRange)]
    [InlineData("?pageSize=1001", PagingQuery.PageSize, PaginationErrorCodes.PageSizeOutOfRange)]
    [InlineData("?page=abc", PagingQuery.Page, ErrorCodes.Validation.InvalidFormat)]
    [InlineData("?page=1.5", PagingQuery.Page, ErrorCodes.Validation.InvalidFormat)]
    [InlineData("?page=99999999999", PagingQuery.Page, ErrorCodes.Validation.InvalidFormat)]
    [InlineData("?page=1&page=2", PagingQuery.Page, ErrorCodes.Validation.InvalidFormat)]
    [InlineData("?pageSize=ten", PagingQuery.PageSize, ErrorCodes.Validation.InvalidFormat)]
    public async Task Paging_WithAnInvalidParameter_Is400_KeyedByTheParameter_BeforeTheHandler(
        string query,
        string field,
        string code)
    {
        using var response = await Client.GetAsync("/items" + query);

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, ErrorCodes.Validation.Failed);
        FieldCodes(problem).Should().BeEquivalentTo(new Dictionary<string, string[]> { [field] = [code] });
        problem.GetProperty(ProblemDetailsExtensionNames.Errors).GetProperty(field)[0].GetString().Should().NotBeNullOrWhiteSpace();
        _calls.Should().Be(0);
    }

    [Fact]
    public async Task Paging_WithBothParametersInvalid_ReportsBoth_InQueryOrder()
    {
        using var response = await Client.GetAsync("/items?pageSize=5000&page=x");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, ErrorCodes.Validation.Failed);
        FieldCodes(problem).Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            [PagingQuery.Page] = [ErrorCodes.Validation.InvalidFormat],
            [PagingQuery.PageSize] = [PaginationErrorCodes.PageSizeOutOfRange],
        });
        problem.GetProperty(ProblemDetailsExtensionNames.ErrorCodes).EnumerateObject().Select(member => member.Name)
            .Should().Equal(PagingQuery.Page, PagingQuery.PageSize);
        _calls.Should().Be(0);
    }

    [Fact]
    public async Task CursorPaging_WithoutParameters_IsTheFirstPage()
    {
        var answer = await Client.GetFromJsonAsync<CursorAnswer>("/feed");

        answer.Should().Be(new CursorAnswer(null, CursorPageRequest.DefaultLimit));
    }

    [Fact]
    public async Task CursorPaging_WithValidParameters_BindsThem()
    {
        var cursor = PageCursor.Encode(42L, 7L);

        var answer = await Client.GetFromJsonAsync<CursorAnswer>($"/feed?cursor={Uri.EscapeDataString(cursor)}&limit=5");

        answer.Should().Be(new CursorAnswer(cursor, 5));
    }

    [Theory]
    [InlineData("?limit=0", PagingQuery.Limit, PaginationErrorCodes.LimitOutOfRange)]
    [InlineData("?limit=1001", PagingQuery.Limit, PaginationErrorCodes.LimitOutOfRange)]
    [InlineData("?limit=many", PagingQuery.Limit, ErrorCodes.Validation.InvalidFormat)]
    [InlineData("?cursor=%20%20", PagingQuery.Cursor, PaginationErrorCodes.CursorInvalid)]
    [InlineData("?cursor=a&cursor=b", PagingQuery.Cursor, ErrorCodes.Validation.InvalidFormat)]
    public async Task CursorPaging_WithAnInvalidParameter_Is400_KeyedByTheParameter_BeforeTheHandler(
        string query,
        string field,
        string code)
    {
        using var response = await Client.GetAsync("/feed" + query);

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, ErrorCodes.Validation.Failed);
        FieldCodes(problem).Should().BeEquivalentTo(new Dictionary<string, string[]> { [field] = [code] });
        _calls.Should().Be(0);
    }

    [Fact]
    public async Task CursorPaging_WithAnOverlongCursor_Is400_CursorInvalid()
    {
        using var response = await Client.GetAsync("/feed?cursor=" + new string('a', PageCursor.MaxLength + 1));

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, ErrorCodes.Validation.Failed);
        FieldCodes(problem).Should().BeEquivalentTo(new Dictionary<string, string[]> { [PagingQuery.Cursor] = [PaginationErrorCodes.CursorInvalid] });
        _calls.Should().Be(0);
    }

    [Fact]
    public async Task BindAsync_WithoutThePipeline_RefusesInvalidInput_With400()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?pageSize=0");

        var act = async () => await Paging.BindAsync(context);

        (await act.Should().ThrowAsync<BadHttpRequestException>()).Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void Constructors_RefuseNull()
    {
        FluentActions.Invoking(() => new Paging(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new CursorPaging(null!)).Should().Throw<ArgumentNullException>();
    }

    private static Dictionary<string, string[]> FieldCodes(JsonElement problem) =>
        problem.GetProperty(ProblemDetailsExtensionNames.ErrorCodes).EnumerateObject().ToDictionary(
            member => member.Name,
            member => member.Value.EnumerateArray().Select(code => code.GetString()!).ToArray());

    private sealed record PageAnswer(int Page, int PageSize);

    private sealed record CursorAnswer(string? Cursor, int Limit);
}
