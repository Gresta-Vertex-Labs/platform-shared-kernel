using FluentAssertions;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Storage.Abstractions.Tests.Models;

/// <summary>T-02: <see cref="PresignedUrlRequest"/> value equality.</summary>
public sealed class PresignedUrlRequestTests
{
    [Fact]
    public void TwoInstances_WithSameValues_AreEqual()
    {
        var first = new PresignedUrlRequest { Bucket = "bucket", Key = "key", Expiry = TimeSpan.FromMinutes(15) };
        var second = new PresignedUrlRequest { Bucket = "bucket", Key = "key", Expiry = TimeSpan.FromMinutes(15) };

        first.Should().Be(second);
    }

    [Fact]
    public void TwoInstances_WithDifferentExpiry_AreNotEqual()
    {
        var first = new PresignedUrlRequest { Bucket = "bucket", Key = "key", Expiry = TimeSpan.FromMinutes(15) };
        var second = new PresignedUrlRequest { Bucket = "bucket", Key = "key", Expiry = TimeSpan.FromMinutes(30) };

        first.Should().NotBe(second);
    }
}
