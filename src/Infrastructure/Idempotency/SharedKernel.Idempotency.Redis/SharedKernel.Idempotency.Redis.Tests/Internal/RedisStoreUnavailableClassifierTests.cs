using SharedKernel.Idempotency.Redis.Internal;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Internal;

public sealed class RedisStoreUnavailableClassifierTests
{
    [Theory]
    [InlineData(typeof(TimeoutException))]
    public void IsStoreUnavailable_ForConnectivityExceptions_ReturnsTrue(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.True(RedisStoreUnavailableClassifier.IsStoreUnavailable(exception));
    }

    [Fact]
    public void IsStoreUnavailable_ForRedisConnectionException_ReturnsTrue()
    {
        var exception = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "unreachable");

        Assert.True(RedisStoreUnavailableClassifier.IsStoreUnavailable(exception));
    }

    [Fact]
    public void IsStoreUnavailable_ForRedisTimeoutException_ReturnsTrue()
    {
        var exception = new RedisTimeoutException("timed out", CommandStatus.Unknown);

        Assert.True(RedisStoreUnavailableClassifier.IsStoreUnavailable(exception));
    }

    [Fact]
    public void IsStoreUnavailable_ForArgumentException_ReturnsFalse()
    {
        // A programming-error/data-shape defect must never be misclassified as a store-availability
        // problem — that would silently let a real bug through the fail-open path.
        var exception = new ArgumentException("bad argument");

        Assert.False(RedisStoreUnavailableClassifier.IsStoreUnavailable(exception));
    }

    [Fact]
    public void IsStoreUnavailable_ForInvalidOperationException_ReturnsFalse()
    {
        var exception = new InvalidOperationException("not a connectivity problem");

        Assert.False(RedisStoreUnavailableClassifier.IsStoreUnavailable(exception));
    }
}
