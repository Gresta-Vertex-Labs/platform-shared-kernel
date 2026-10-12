using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication.Tests;

public sealed class AccessTokenUnavailableExceptionTests
{
    private static readonly Error Failure =
        Error.Unavailable(CommunicationErrorCodes.AccessTokenUnavailable, "The token endpoint could not be reached.");

    [Fact]
    public void It_is_a_kernel_exception_the_edges_map_to_unavailable()
    {
        var exception = new AccessTokenUnavailableException("orders", Failure);

        exception.Should().BeAssignableTo<SharedKernelException>();
        exception.Should().BeAssignableTo<IHasError>();
        exception.Error.Type.Should().Be(ErrorType.Unavailable);
        exception.Error.Code.Should().Be("communication.access_token_unavailable");
        exception.Message.Should().Be(Failure.Message);
    }

    [Fact]
    public void It_is_no_longer_an_http_request_exception()
    {
        typeof(AccessTokenUnavailableException).IsAssignableTo(typeof(HttpRequestException)).Should().BeFalse();
    }

    [Fact]
    public void The_handler_constructor_records_the_client_and_the_error_constructors_do_not()
    {
        new AccessTokenUnavailableException("orders", Failure).ClientName.Should().Be("orders");
        new AccessTokenUnavailableException(Failure).ClientName.Should().BeNull();
        new AccessTokenUnavailableException(Failure, new InvalidOperationException("x")).ClientName.Should().BeNull();
    }

    [Fact]
    public void The_inner_exception_is_kept()
    {
        var inner = new InvalidOperationException("boom");

        var exception = new AccessTokenUnavailableException(Failure, inner);

        exception.InnerException.Should().BeSameAs(inner);
        exception.Error.Should().BeSameAs(Failure);
    }
}
