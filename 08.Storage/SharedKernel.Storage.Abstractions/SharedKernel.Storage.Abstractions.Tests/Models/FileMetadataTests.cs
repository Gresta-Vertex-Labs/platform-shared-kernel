using FluentAssertions;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Storage.Abstractions.Tests.Models;

/// <summary>T-02: <see cref="FileMetadata"/> value equality.</summary>
public sealed class FileMetadataTests
{
    private static readonly DateTimeOffset FixedInstant = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TwoInstances_WithSameValues_AreEqual()
    {
        var first = new FileMetadata
        {
            Bucket = "bucket",
            Key = "key",
            ContentType = "application/pdf",
            ContentLength = 1024,
            LastModified = FixedInstant,
            ETag = "etag-1",
        };

        var second = new FileMetadata
        {
            Bucket = "bucket",
            Key = "key",
            ContentType = "application/pdf",
            ContentLength = 1024,
            LastModified = FixedInstant,
            ETag = "etag-1",
        };

        first.Should().Be(second);
    }

    [Fact]
    public void TwoInstances_WithDifferentContentLength_AreNotEqual()
    {
        var first = new FileMetadata { Bucket = "b", Key = "k", ContentType = "t", ContentLength = 1, LastModified = FixedInstant };
        var second = new FileMetadata { Bucket = "b", Key = "k", ContentType = "t", ContentLength = 2, LastModified = FixedInstant };

        first.Should().NotBe(second);
    }

    [Fact]
    public void ETag_IsOptional_AndDefaultsToNull()
    {
        var metadata = new FileMetadata { Bucket = "b", Key = "k", ContentType = "t", ContentLength = 0, LastModified = FixedInstant };

        metadata.ETag.Should().BeNull();
    }
}
