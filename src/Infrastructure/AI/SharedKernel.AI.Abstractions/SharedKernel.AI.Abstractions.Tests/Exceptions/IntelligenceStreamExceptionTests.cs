using FluentAssertions;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Abstractions.Tests.Exceptions;

/// <summary>
/// <see cref="IntelligenceStreamException"/> is a sealed <see cref="SharedKernelException"/> (so the HTTP, gRPC and
/// SignalR edges map it by its <see cref="Error"/>) and an <see cref="IHasError"/> (so the span recorder reads its code).
/// </summary>
public sealed class IntelligenceStreamExceptionTests
{
    [Fact]
    public void IsASealedSharedKernelException_AndIHasError()
    {
        typeof(IntelligenceStreamException).IsSealed.Should().BeTrue();
        typeof(IntelligenceStreamException).BaseType.Should().Be(typeof(SharedKernelException));
        typeof(IHasError).IsAssignableFrom(typeof(IntelligenceStreamException)).Should().BeTrue();
    }

    [Fact]
    public void ErrorConstructor_CarriesTheError_AndMessageIsTheErrorMessage()
    {
        var error = IntelligenceErrors.Unavailable("qdrant");

        var exception = new IntelligenceStreamException(error);

        exception.Error.Should().BeSameAs(error);
        ((IHasError)exception).Error.Should().BeSameAs(error);
        exception.Message.Should().Be(error.Message);
        exception.InnerException.Should().BeNull();
    }

    [Fact]
    public void ErrorAndInnerConstructor_KeepsTheInnerInstance_WithoutCopyingItsText()
    {
        var error = IntelligenceErrors.Timeout("scroll");
        var inner = new InvalidOperationException("host-sentinel provider detail");

        var exception = new IntelligenceStreamException(error, inner);

        exception.InnerException.Should().BeSameAs(inner);
        exception.Message.Should().Be(error.Message).And.NotContain("host-sentinel");
        exception.Error.Type.Should().Be(ErrorType.Timeout);
    }

    [Fact]
    public void Constructors_RejectANullError()
    {
        var withError = () => new IntelligenceStreamException(null!);
        var withInner = () => new IntelligenceStreamException(null!, new InvalidOperationException());

        withError.Should().Throw<ArgumentNullException>();
        withInner.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ExposesNoBareStringConstructor()
    {
        typeof(IntelligenceStreamException).GetConstructors()
            .Should().OnlyContain(c => c.GetParameters()[0].ParameterType == typeof(Error));
    }
}
