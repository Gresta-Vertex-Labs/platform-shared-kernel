using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Errors;

/// <summary>Design D1 and B15: the problem body of an <see cref="Error"/>, built with the request at hand.</summary>
public sealed class ErrorProblemDetailsExtensionsTests
{
    [Fact]
    public void ToProblemDetails_HasTheD1Members()
    {
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        context.Request.Path = "/orders/42";

        var problem = TestErrors.OrderNotFound.ToProblemDetails(context);

        problem.Status.Should().Be(StatusCodes.Status404NotFound);
        problem.Title.Should().Be("Not Found");
        problem.Type.Should().Be("https://tools.ietf.org/html/rfc9110#section-15.5.5");
        problem.Detail.Should().Be(TestErrors.OrderNotFound.Message);
        problem.Instance.Should().Be("/orders/42");
        problem.Extensions[ProblemDetailsExtensionNames.ErrorCode].Should().Be("order.not_found");
        problem.Extensions[ProblemDetailsExtensionNames.TraceId].Should().NotBeNull();
        problem.Extensions.Should().NotContainKey(ProblemDetailsExtensionNames.Errors);
    }

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Unexpected)]
    [InlineData(ErrorType.Unavailable)]
    [InlineData(ErrorType.Timeout)]
    public void B15_TitleIsTheReasonPhrase_AndTypeIsNeverAThirdPartySite(ErrorType type)
    {
        var problem = new Error("some.code", "Message.", type).ToProblemDetails(new DefaultHttpContext());

        problem.Title.Should().NotBe("some.code").And.NotBeNullOrWhiteSpace();
        problem.Type.Should().StartWith("https://tools.ietf.org/").And.NotContain("httpstatuses");
    }

    [Fact]
    public void ToProblemDetails_ListsTheDetailsOfAnAggregate_ByField()
    {
        var name = Error.Validation("customer.name_required", "Name is required.") with
        {
            MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = "Name" },
        };
        var unnamed = Error.Validation("customer.blocked", "Customer is blocked.");

        var problem = Error.Validation([name, unnamed]).ToProblemDetails(new DefaultHttpContext());

        var errors = problem.Extensions[ProblemDetailsExtensionNames.Errors].Should().BeAssignableTo<IDictionary<string, string[]>>().Subject;
        errors["Name"].Should().Equal("Name is required.");
        errors["customer.blocked"].Should().Equal("Customer is blocked.");
        var codes = problem.Extensions[ProblemDetailsExtensionNames.ErrorCodes].Should().BeAssignableTo<IDictionary<string, string[]>>().Subject;
        codes["Name"].Should().Equal("customer.name_required");
    }

    [Fact]
    public void ToProblemDetails_RequiresTheRequest()
    {
        var act = () => TestErrors.OrderNotFound.ToProblemDetails(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
