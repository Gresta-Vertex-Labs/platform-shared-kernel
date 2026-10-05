using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using Xunit;

namespace SharedKernel.Presentation.Core.Tests.Http;

/// <summary>
/// P-579: the correlation id every presentation package reports (problem bodies, gRPC <c>ErrorInfo</c>, hub errors) is
/// the one held by the call's <see cref="RequestContextScope"/>, which <c>UseSharedKernelRequestContext()</c> opens.
/// </summary>
public sealed class RequestFactsTests
{
    [Fact]
    public void GetCorrelationId_ReadsTheAmbientScope()
    {
        using (RequestContextScope.Begin(new SystemRequestContext([], correlationId: "flow-1")))
        {
            RequestFacts.GetCorrelationId(new DefaultHttpContext()).Should().Be("flow-1");
            RequestFacts.GetCorrelationId(null).Should().Be("flow-1");
        }
    }

    [Fact]
    public void GetCorrelationId_AsksTheRegisteredAccessorFirst()
    {
        var services = new ServiceCollection()
            .AddSingleton<IRequestContextAccessor>(new FixedAccessor(new SystemRequestContext([], correlationId: "flow-2")))
            .BuildServiceProvider();

        RequestFacts.GetCorrelationId(new DefaultHttpContext { RequestServices = services }).Should().Be("flow-2");
    }

    [Fact]
    public void GetCorrelationId_IsNull_OutsideAnyScope()
    {
        RequestFacts.GetCorrelationId(new DefaultHttpContext()).Should().BeNull();
    }

    [Theory]
    [InlineData("application/grpc", true)]
    [InlineData("application/grpc+proto", true)]
    [InlineData("application/json", false)]
    [InlineData(null, false)]
    public void IsGrpcRequest_ReadsTheContentType(string? contentType, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = contentType;

        RequestFacts.IsGrpcRequest(context).Should().Be(expected);
    }

    private sealed class FixedAccessor(IRequestContext context) : IRequestContextAccessor
    {
        public IRequestContext? Current => context;
    }
}
