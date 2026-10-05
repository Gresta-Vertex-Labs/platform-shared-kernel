using System.Net.Sockets;
using Npgsql;
using SharedKernel.Idempotency.EfCore.Internal;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Internal;

public sealed class EfCoreStoreUnavailableClassifierTests
{
    [Fact]
    public void IsStoreUnavailable_ForTimeoutException_ReturnsTrue()
    {
        Assert.True(EfCoreStoreUnavailableClassifier.IsStoreUnavailable(new TimeoutException()));
    }

    [Fact]
    public void IsStoreUnavailable_ForSocketException_ReturnsTrue()
    {
        Assert.True(EfCoreStoreUnavailableClassifier.IsStoreUnavailable(new SocketException()));
    }

    [Fact]
    public void IsStoreUnavailable_ForNpgsqlExceptionWrappingSocketException_ReturnsTrue()
    {
        var exception = new NpgsqlException("connection refused", new SocketException());

        Assert.True(EfCoreStoreUnavailableClassifier.IsStoreUnavailable(exception));
    }

    [Fact]
    public void IsStoreUnavailable_ForArgumentException_ReturnsFalse()
    {
        // A programming-error/data-shape defect must never be misclassified as a store-availability
        // problem — that would silently let a real bug through the fail-open path.
        Assert.False(EfCoreStoreUnavailableClassifier.IsStoreUnavailable(new ArgumentException("bad argument")));
    }

    [Fact]
    public void IsStoreUnavailable_ForInvalidOperationException_ReturnsFalse()
    {
        Assert.False(EfCoreStoreUnavailableClassifier.IsStoreUnavailable(new InvalidOperationException("not a connectivity problem")));
    }

    [Fact]
    public void IsStoreUnavailable_ForExecutionStrategyWrappedConnectivityFailure_ReturnsTrue()
    {
        // Reproduces EF Core's NpgsqlExecutionStrategy wrapping shape: InvalidOperationException
        // -> NpgsqlException -> TimeoutException. The real connectivity failure is two levels down
        // from the exception type actually caught at the store's call site.
        var timeout = new TimeoutException("Timeout during connection attempt");
        var npgsql = new NpgsqlException("Failed to connect to 127.0.0.1:1", timeout);
        var wrapped = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.", npgsql);

        Assert.True(EfCoreStoreUnavailableClassifier.IsStoreUnavailable(wrapped));
    }

    [Fact]
    public void IsStoreUnavailable_ForInvalidOperationExceptionWrappingUnrelatedError_ReturnsFalse()
    {
        var wrapped = new InvalidOperationException("outer", new ArgumentException("inner, unrelated"));

        Assert.False(EfCoreStoreUnavailableClassifier.IsStoreUnavailable(wrapped));
    }
}
