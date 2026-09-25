using System.Text.Json.Nodes;
using Asp.Versioning;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Presentation.OpenApi.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Documents;

/// <summary>
/// P-563 P4: an endpoint taking a <see cref="Paging"/> or <see cref="CursorPaging"/> parameter documents the two optional
/// query parameters it binds, with their ranges, and the 400 problem the core answers for invalid input; the parameter
/// itself is never documented as a body or a query value of its own.
/// </summary>
public sealed class PagingDocumentationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private JsonNode _document = null!;

    public async Task InitializeAsync()
    {
        _app = await OpenApiTestHost.StartAsync(app =>
        {
            var invoices = app.NewVersionedApi("Invoices").MapGroup("/v{version:apiVersion}/invoices").HasApiVersion(1.0);
            invoices.MapGet("/", (Paging paging) => paging.Request.Page);
            invoices.MapGet("/feed", (CursorPaging paging) => paging.Request.Limit);
            invoices.MapGet("/count", () => 3);
        });
        using var client = _app.GetTestClient();
        _document = await client.GetDocumentAsync("v1");
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public void Paging_DocumentsPageAndPageSize_AsOptionalIntegers()
    {
        var operation = _document.Operation("/v1/invoices", "get");

        Names(operation).Should().Equal("query page", "query pageSize");

        var page = Query(operation, "page");
        IsRequired(page).Should().BeFalse();
        page["schema"]!["type"]!.GetValue<string>().Should().Be("integer");
        page["schema"]!["minimum"]!.GetValue<int>().Should().Be(1);
        page["schema"]!["default"]!.GetValue<int>().Should().Be(1);

        var pageSize = Query(operation, "pageSize");
        IsRequired(pageSize).Should().BeFalse();
        pageSize["schema"]!["minimum"]!.GetValue<int>().Should().Be(1);
        pageSize["schema"]!["maximum"]!.GetValue<int>().Should().Be(PageRequest.MaxPageSize);
        pageSize["schema"]!["default"]!.GetValue<int>().Should().Be(PageRequest.DefaultPageSize);

        operation["requestBody"].Should().BeNull("a Paging parameter is never inferred as the body");
    }

    [Fact]
    public void CursorPaging_DocumentsCursorAndLimit_AsOptional()
    {
        var operation = _document.Operation("/v1/invoices/feed", "get");

        Names(operation).Should().Equal("query cursor", "query limit");

        var cursor = Query(operation, "cursor");
        IsRequired(cursor).Should().BeFalse();
        cursor["schema"]!["type"]!.GetValue<string>().Should().Be("string");
        cursor["schema"]!["maxLength"]!.GetValue<int>().Should().Be(PageCursor.MaxLength);

        var limit = Query(operation, "limit");
        IsRequired(limit).Should().BeFalse();
        limit["schema"]!["maximum"]!.GetValue<int>().Should().Be(CursorPageRequest.MaxLimit);
        limit["schema"]!["default"]!.GetValue<int>().Should().Be(CursorPageRequest.DefaultLimit);
    }

    [Theory]
    [InlineData("/v1/invoices", PaginationErrorCodes.PageOutOfRange, PaginationErrorCodes.PageSizeOutOfRange)]
    [InlineData("/v1/invoices/feed", PaginationErrorCodes.CursorInvalid, PaginationErrorCodes.LimitOutOfRange)]
    public void Paging_Documents400_AsAProblem_WithThePaginationCodes(string path, string firstCode, string secondCode)
    {
        var response = _document.Operation(path, "get")["responses"]!["400"]!;

        response["content"]!["application/problem+json"]!["schema"]!["$ref"].Should().NotBeNull();
        response["description"]!.GetValue<string>().Should()
            .Contain(ErrorCodes.Validation.Failed)
            .And.Contain(firstCode)
            .And.Contain(secondCode)
            .And.Contain(ErrorCodes.Validation.InvalidFormat);
    }

    [Fact]
    public void AnEndpointWithoutPaging_DocumentsNoPagingParameters_AndNo400()
    {
        var operation = _document.Operation("/v1/invoices/count", "get");

        operation["parameters"]?.AsArray().Should().BeEmpty();
        operation["responses"]!["400"].Should().BeNull();
    }

    private static string[] Names(JsonNode operation) =>
        [.. operation["parameters"]!.AsArray().Select(parameter => $"{parameter!["in"]!.GetValue<string>()} {parameter["name"]!.GetValue<string>()}")];

    private static JsonNode Query(JsonNode operation, string name) =>
        operation["parameters"]!.AsArray().Single(parameter =>
            parameter!["in"]!.GetValue<string>() == "query" && parameter["name"]!.GetValue<string>() == name)!;

    private static bool IsRequired(JsonNode parameter) => parameter["required"]?.GetValue<bool>() ?? false;
}
