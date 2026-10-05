using Xunit;

namespace SharedKernel.Caching.Abstractions.Tests;

public sealed class DistributedLockContractTests
{
    [Fact]
    public void Options_Default_HasDocumentedSettings()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), DistributedLockOptions.Default.Expiry);
        Assert.Equal(TimeSpan.Zero, DistributedLockOptions.Default.WaitTime);
        Assert.Equal(TimeSpan.FromMilliseconds(200), DistributedLockOptions.Default.RetryInterval);
    }

    [Fact]
    public void Options_InvalidValues_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DistributedLockOptions { Expiry = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new DistributedLockOptions { WaitTime = TimeSpan.FromSeconds(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new DistributedLockOptions { RetryInterval = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => DistributedLockOptions.Default with { Expiry = TimeSpan.FromSeconds(-5) });
    }

    [Fact]
    public void Options_ZeroWaitTime_IsAllowed() =>
        Assert.Equal(TimeSpan.Zero, new DistributedLockOptions { WaitTime = TimeSpan.Zero }.WaitTime);

    [Fact]
    public void Lease_CapturesValues()
    {
        var expiresAt = new DateTimeOffset(2026, 9, 17, 2, 0, 30, TimeSpan.Zero);
        var lease = new DistributedLease("job:nightly:2026-09-17T02:00", 42, expiresAt);

        Assert.Equal("job:nightly:2026-09-17T02:00", lease.Resource);
        Assert.Equal(42, lease.FencingToken);
        Assert.Equal(expiresAt, lease.ExpiresAt);
    }

    [Fact]
    public void Lease_InvalidValues_Throw()
    {
        Assert.ThrowsAny<ArgumentException>(() => new DistributedLease(" ", 1, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DistributedLease("r", 0, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void UnavailableException_ForResource_CarriesResourceAndInner()
    {
        var inner = new TimeoutException();

        DistributedLockUnavailableException exception = DistributedLockUnavailableException.ForResource("invoice:42", inner);

        Assert.Equal("invoice:42", exception.Resource);
        Assert.Same(inner, exception.InnerException);
        Assert.Contains("invoice:42", exception.Message, StringComparison.Ordinal);
    }
}
