using FluentAssertions;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Storage.Abstractions.Tests.Models;

/// <summary>T-02: <see cref="FileUploadRequest"/> value equality.</summary>
public sealed class FileUploadRequestTests
{
    [Fact]
    public void TwoInstances_WithSameValues_AreEqual()
    {
        using var content = new MemoryStream();

        var first = new FileUploadRequest
        {
            Bucket = "bucket",
            Key = "key",
            Content = content,
            ContentType = "application/pdf",
            Metadata = new Dictionary<string, string> { ["a"] = "1" },
        };

        var second = new FileUploadRequest
        {
            Bucket = "bucket",
            Key = "key",
            Content = content,
            ContentType = "application/pdf",
            Metadata = first.Metadata,
        };

        first.Should().Be(second);
        (first == second).Should().BeTrue();
    }

    [Fact]
    public void TwoInstances_WithDifferentKey_AreNotEqual()
    {
        using var content = new MemoryStream();

        var first = new FileUploadRequest { Bucket = "bucket", Key = "key-1", Content = content, ContentType = "text/plain" };
        var second = new FileUploadRequest { Bucket = "bucket", Key = "key-2", Content = content, ContentType = "text/plain" };

        first.Should().NotBe(second);
    }

    [Fact]
    public void Metadata_DefaultsToNull_WhenNotSupplied()
    {
        using var content = new MemoryStream();

        var request = new FileUploadRequest { Bucket = "bucket", Key = "key", Content = content, ContentType = "text/plain" };

        request.Metadata.Should().BeNull();
    }
}
