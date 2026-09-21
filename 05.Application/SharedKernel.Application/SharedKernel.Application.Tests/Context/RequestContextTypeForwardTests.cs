using FluentAssertions;
using SharedKernel.Application.Context;

namespace SharedKernel.Application.Tests.Context;

/// <summary>
/// <see cref="IRequestContext"/> and its two implementations moved to
/// <c>SharedKernel.Application.Abstractions</c>; <c>SharedKernel.Application</c> forwards them so an
/// assembly compiled against the old location still binds.
/// </summary>
public sealed class RequestContextTypeForwardTests
{
    [Theory]
    [InlineData("SharedKernel.Application.Context.IRequestContext")]
    [InlineData("SharedKernel.Application.Context.SystemRequestContext")]
    [InlineData("SharedKernel.Application.Context.AnonymousRequestContext")]
    public void OldAssemblyQualifiedName_ResolvesToTheAbstractionsType(string typeName)
    {
        var resolved = Type.GetType($"{typeName}, SharedKernel.Application", throwOnError: true)!;

        resolved.Assembly.GetName().Name.Should().Be("SharedKernel.Application.Abstractions");
        resolved.Should().BeSameAs(typeof(IRequestContext).Assembly.GetType(typeName));
    }
}
