using FluentAssertions;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Storage.Abstractions.Tests.Models;

/// <summary>T-02: <see cref="FileReference"/> value equality.</summary>
public sealed class FileReferenceTests
{
    [Fact]
    public void TwoInstances_WithSameValues_AreEqual()
    {
        var first = new FileReference { Bucket = "bucket", Key = "key", ETag = "etag-1", VersionId = "v1" };
        var second = new FileReference { Bucket = "bucket", Key = "key", ETag = "etag-1", VersionId = "v1" };

        first.Should().Be(second);
    }

    [Fact]
    public void TwoInstances_WithDifferentETag_AreNotEqual()
    {
        var first = new FileReference { Bucket = "bucket", Key = "key", ETag = "etag-1" };
        var second = new FileReference { Bucket = "bucket", Key = "key", ETag = "etag-2" };

        first.Should().NotBe(second);
    }

    [Fact]
    public void ETag_And_VersionId_AreOptional_AndDefaultToNull()
    {
        var reference = new FileReference { Bucket = "bucket", Key = "key" };

        reference.ETag.Should().BeNull();
        reference.VersionId.Should().BeNull();
    }
}
