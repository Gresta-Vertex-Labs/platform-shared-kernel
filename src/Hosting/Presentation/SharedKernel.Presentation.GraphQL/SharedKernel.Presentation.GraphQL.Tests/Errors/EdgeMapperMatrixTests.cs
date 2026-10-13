using FluentAssertions.Execution;
using SharedKernel.Presentation.EdgeCases;
using SharedKernel.Presentation.GraphQL.Tests.TestSupport;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.GraphQL.Tests.Errors;

/// <summary>
/// SK.14.PresentationConformance: the edge-mapper conformance matrix over GraphQL. Every <c>SharedKernelException</c>
/// another domain throws, thrown from a resolver, is answered with the status and error code its type and owner define,
/// with the trace id and the correlation id; outside Development the message of a server-category case is replaced.
/// </summary>
public sealed class EdgeMapperMatrixTests
{
    [Theory]
    [InlineData(GraphQLTestHost.Production)]
    [InlineData(GraphQLTestHost.Development)]
    public async Task EveryCase_IsAnsweredWithItsStatusAndCode(string environment)
    {
        await using var app = await GraphQLTestHost.StartAsync(environment);

        foreach (var edgeCase in EdgeMapperCatalogue.Cases)
        {
            using var scope = new AssertionScope(edgeCase.Name);
            using var result = await app.PostAsync($"{{ edge(name: \"{edgeCase.Name}\") }}");

            var extensions = result.Extensions();
            extensions.GetProperty("status").GetInt32().Should().Be(edgeCase.ExpectedHttpStatus);
            extensions.GetProperty("errorCode").GetString().Should().Be(edgeCase.ExpectedCode);
            extensions.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
            extensions.GetProperty("correlationId").GetString().Should().Be(GraphQLTestHost.CorrelationId);

            var detail = extensions.GetProperty("detail").GetString();
            var original = edgeCase.CreateError().Message;

            if (edgeCase.IsServerCategory && environment == GraphQLTestHost.Production)
            {
                detail.Should().NotBe(original);
                result.Text.Should().NotContain("db-7.internal");
            }
            else
            {
                detail.Should().Be(original);
            }
        }
    }

    // The status GraphQL answers is Core's table, for every error type there is: it cannot drift from HTTP's.
    [Fact]
    public async Task EveryErrorType_IsAnsweredWithTheStatusOfCoresTable()
    {
        await using var app = await GraphQLTestHost.StartAsync();

        foreach (var type in Enum.GetValues<ErrorType>().Where(type => type != ErrorType.None))
        {
            using var scope = new AssertionScope(type.ToString());
            using var result = await app.PostAsync($"{{ fail(type: \"{type}\") }}");

            result.Extensions().GetProperty("status").GetInt32().Should().Be(ErrorTypeStatusCodeMap.Resolve(type));
        }
    }

    // A plain exception, and one that exposes an Error without deriving SharedKernelException, are defects: both are a
    // 500 unexpected.default, and the second one's own code never reaches the client.
    [Theory]
    [InlineData("plain")]
    [InlineData("error-bearing")]
    public async Task AnExceptionThatIsNotASharedKernelException_IsAnUnexpectedFailure_WhateverItCarries(string kind)
    {
        await using var app = await GraphQLTestHost.StartAsync();

        using var result = await app.PostAsync($"{{ defect(kind: \"{kind}\") }}");

        var extensions = result.Extensions();
        extensions.GetProperty("status").GetInt32().Should().Be(500);
        extensions.GetProperty("errorCode").GetString().Should().Be(ErrorCodes.Unexpected.Default);
        result.Text.Should().NotContain("defect.error_bearing").And.NotContain("hunter2");
    }
}
