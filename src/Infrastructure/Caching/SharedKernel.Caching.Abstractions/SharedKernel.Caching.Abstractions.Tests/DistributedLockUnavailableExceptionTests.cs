using SharedKernel.Caching.Abstractions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Caching.Abstractions.Tests;

/// <summary>
/// <see cref="DistributedLockUnavailableException"/> is a classified <see cref="SharedKernelException"/>: edges map it
/// through its <see cref="Error"/> (HTTP 503 / gRPC Unavailable), so the type, code and message are pinned here.
/// </summary>
public sealed class DistributedLockUnavailableExceptionTests
{
    [Fact]
    public void ForResource_IsSharedKernelException_AndIHasError_WithUnavailableError()
    {
        var exception = DistributedLockUnavailableException.ForResource("invoice:42");

        Assert.IsAssignableFrom<SharedKernelException>(exception);
        Assert.IsAssignableFrom<IHasError>(exception);
        Assert.Equal(ErrorType.Unavailable, exception.Error.Type);
        Assert.Equal("caching.lock_unavailable", exception.Error.Code);
    }

    [Fact]
    public void ErrorCode_LiteralIsPinned()
    {
        Assert.Equal("caching.lock_unavailable", CachingErrorCodes.LockUnavailable);
    }

    [Fact]
    public void ForResource_MessageIsTheFixedErrorMessage_AndNeverContainsTheResource()
    {
        var exception = DistributedLockUnavailableException.ForResource("invoice:42");

        Assert.Equal(exception.Error.Message, exception.Message);
        Assert.Equal("The distributed lock store is unavailable.", exception.Message);
        Assert.DoesNotContain("invoice:42", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ForResource_WithInner_PreservesResourceAndInner()
    {
        var inner = new TimeoutException();

        var exception = DistributedLockUnavailableException.ForResource("invoice:42", inner);

        Assert.Equal("invoice:42", exception.Resource);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void ForResource_WithoutInner_HasNoInner()
    {
        var exception = DistributedLockUnavailableException.ForResource("invoice:42");

        Assert.Equal("invoice:42", exception.Resource);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Constructors_NullError_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new DistributedLockUnavailableException(null!));
        Assert.Throws<ArgumentNullException>(() => new DistributedLockUnavailableException(null!, new TimeoutException()));
    }

    [Fact]
    public void Constructors_WithoutResource_LeaveResourceNull()
    {
        var error = Error.Unavailable(CachingErrorCodes.LockUnavailable, "The distributed lock store is unavailable.");
        var inner = new TimeoutException();

        var plain = new DistributedLockUnavailableException(error);
        var wrapped = new DistributedLockUnavailableException(error, inner);

        Assert.Null(plain.Resource);
        Assert.Same(error, plain.Error);
        Assert.Same(inner, wrapped.InnerException);
    }
}
