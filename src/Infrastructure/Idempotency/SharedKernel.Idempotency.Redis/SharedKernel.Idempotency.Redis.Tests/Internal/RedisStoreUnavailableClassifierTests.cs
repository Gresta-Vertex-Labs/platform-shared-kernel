using SharedKernel.Idempotency.Redis.Internal;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Internal;

public sealed class RedisStoreUnavailableClassifierTests
{
    [Fact]
    public void IsStoreUnavailable_ForTimeoutException_ReturnsTrue() =>
        Assert.True(RedisStoreUnavailableClassifier.IsStoreUnavailable(new TimeoutException()));

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

    [Theory]
    [InlineData("LOADING Redis is loading the dataset in memory")]
    [InlineData("BUSY Redis is busy running a script. You can only call SCRIPT KILL or SHUTDOWN NOSAVE.")]
    [InlineData("READONLY You can't write against a read only replica.")]
    [InlineData("MASTERDOWN Link with MASTER is down and replica-serve-stale-data is set to 'no'.")]
    [InlineData("CLUSTERDOWN The cluster is down")]
    [InlineData("TRYAGAIN Multiple keys request during rehashing of slot")]
    [InlineData("OOM command not allowed when used memory > 'maxmemory'.")]
    public void IsStoreUnavailable_ForATransientServerState_ReturnsTrue(string reply) =>
        Assert.True(RedisStoreUnavailableClassifier.IsStoreUnavailable(new RedisServerException(reply)));

    [Theory]
    [InlineData("ERR Error running script (call to f_abc): @user_script:1: boom")]
    [InlineData("ERR syntax error")]
    [InlineData("WRONGTYPE Operation against a key holding the wrong kind of value")]
    [InlineData("NOSCRIPT No matching script. Please use EVAL.")]
    [InlineData("BUSYKEY Target key name already exists.")]
    [InlineData("NOAUTH Authentication required.")]
    [InlineData("")]
    public void IsStoreUnavailable_ForAScriptOrDataDefectReply_ReturnsFalse(string reply) =>
        Assert.False(RedisStoreUnavailableClassifier.IsStoreUnavailable(new RedisServerException(reply)));

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

    [Fact]
    public void IsStoreUnavailable_ForOperationCanceledException_ReturnsFalse() =>
        Assert.False(RedisStoreUnavailableClassifier.IsStoreUnavailable(new OperationCanceledException()));
}
