using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Exceptions;

namespace SharedKernel.Search.Abstractions.Tests.Exceptions;

/// <summary>
/// <see cref="SearchStreamException"/> is a sealed <see cref="SharedKernelException"/> (and so
/// <see cref="IHasError"/>): the HTTP, gRPC and SignalR edges translate it by its <see cref="Error"/> type
/// instead of answering 500 and losing the error.
/// </summary>
public sealed class SearchStreamExceptionTests
{
    [Fact]
    public void IsASealedSharedKernelException_AndIHasError()
    {
        typeof(SearchStreamException).BaseType.Should().Be(typeof(SharedKernelException));
        typeof(SearchStreamException).IsSealed.Should().BeTrue();
        typeof(IHasError).IsAssignableFrom(typeof(SearchStreamException)).Should().BeTrue();
    }

    [Fact]
    public void ErrorConstructor_CarriesTheError_AndUsesItsMessage()
    {
        var error = SearchErrors.Unreachable("elasticsearch");

        var exception = new SearchStreamException(error);

        exception.Error.Should().BeSameAs(error);
        ((IHasError)exception).Error.Should().BeSameAs(error);
        exception.Message.Should().Be(error.Message);
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void ErrorAndInnerConstructor_KeepsTheInnerExceptionInstance()
    {
        var error = SearchErrors.Timeout("enumerate", TimeSpan.Zero);
        var inner = new TimeoutException("engine detail that must stay out of the error");

        var exception = new SearchStreamException(error, inner);

        exception.Error.Should().BeSameAs(error);
        exception.Message.Should().Be(error.Message).And.NotContain("engine detail");
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("timeout")]
    [InlineData("unexpected")]
    public void ErrorType_IsPreservedForTheEdgeMapping(string kind)
    {
        var error = kind switch
        {
            "unavailable" => SearchErrors.Unreachable("meilisearch"),
            "timeout" => SearchErrors.Timeout("enumerate", TimeSpan.Zero),
            _ => SearchErrors.EngineFault("meilisearch", "enumerate"),
        };

        var exception = new SearchStreamException(error);

        exception.Error.Type.Should().Be(error.Type);
    }

    [Fact]
    public void Constructors_RejectANullError()
    {
        var act = () => new SearchStreamException(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
