using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.EdgeCases;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// SK.14.PresentationConformance: the edge-mapper conformance matrix over HTTP. Every <see cref="SharedKernelException"/>
/// another domain throws, thrown from an endpoint and returned as a failed result, is answered with the status and the
/// code its error type and owner define, as a problem with the trace and correlation ids; outside Development the
/// message of a server-category case is replaced and an unavailable one asks the client to retry.
/// </summary>
public sealed class EdgeMapperMatrixTests
{
    private const string Thrown = "throw";

    private const string Returned = "result";

    [Fact]
    public void TheCatalogue_IsExactlyWhatTheOwnersBuild()
    {
        EdgeMapperCatalogue.Cases.Select(edgeCase => edgeCase.Name).Should().OnlyHaveUniqueItems();

        foreach (var edgeCase in EdgeMapperCatalogue.Cases)
        {
            var exception = edgeCase.CreateException();

            exception.Error.Type.Should().Be(edgeCase.ExpectedType, edgeCase.Name);
            exception.Error.Code.Should().Be(edgeCase.ExpectedCode, edgeCase.Name);
            exception.Should().BeAssignableTo<SharedKernelException>(edgeCase.Name);
        }
    }

    [Theory]
    [InlineData(WebApiTestHost.Production, Thrown)]
    [InlineData(WebApiTestHost.Production, Returned)]
    [InlineData(WebApiTestHost.Development, Thrown)]
    [InlineData(WebApiTestHost.Development, Returned)]
    public async Task EveryCase_IsAnsweredWithItsStatusAndCode(string environment, string mode)
    {
        await using var app = await WebApiTestHost.StartAsync(
            Map,
            configureOptions: options => options.Problems.UnavailableRetryAfter = TimeSpan.FromSeconds(30),
            environment: environment);
        using var client = app.GetTestClient();

        foreach (var edgeCase in EdgeMapperCatalogue.Cases)
        {
            using var scope = new AssertionScope(edgeCase.Name);
            using var response = await client.GetAsync($"/edge/{mode}/{edgeCase.Name}");

            var problem = await response.ShouldBeProblemAsync(edgeCase.ExpectedHttpStatus, edgeCase.ExpectedCode);

            var original = edgeCase.CreateError().Message;
            var detail = problem.Detail();

            if (edgeCase.IsServerCategory && environment == WebApiTestHost.Production)
            {
                detail.Should().NotBe(original);
                detail.Should().NotContain("db-7.internal");
            }
            else
            {
                detail.Should().Be(original);
            }

            if (edgeCase.ExpectedHttpStatus == StatusCodes.Status503ServiceUnavailable)
            {
                response.Headers.RetryAfter?.Delta.Should().Be(TimeSpan.FromSeconds(30));
            }
        }
    }

    // A plain exception and a type that exposes an Error without deriving SharedKernelException are defects: both are
    // answered 500 unexpected.default, and the second one's own code never reaches the body.
    [Theory]
    [InlineData("plain")]
    [InlineData("error-bearing")]
    public async Task AnExceptionThatIsNotASharedKernelException_IsAnUnexpectedFailure_WhateverItCarries(string kind)
    {
        await using var app = await WebApiTestHost.StartAsync(
            endpoints => endpoints.MapGet("/defect/{kind}", IResult (string kind) =>
                throw (kind == "plain" ? new InvalidOperationException("db-7.internal") : new ErrorBearingException())),
            environment: WebApiTestHost.Production);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync($"/defect/{kind}");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status500InternalServerError, ErrorCodes.Unexpected.Default);
        problem.GetRawText().Should().NotContain(ErrorBearingException.Code).And.NotContain("db-7.internal");
    }

    private static void Map(WebApplication app)
    {
        app.MapGet("/edge/throw/{name}", IResult (string name) => throw EdgeMapperCatalogue.Find(name).CreateException());
        app.MapGet("/edge/result/{name}", (string name) => EdgeMapperCatalogue.Find(name).CreateError().ToErrorResult());
    }
}
