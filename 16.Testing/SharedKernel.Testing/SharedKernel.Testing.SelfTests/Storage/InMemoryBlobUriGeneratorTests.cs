using SharedKernel.Storage.Abstractions.Errors;
using SharedKernel.Storage.Abstractions.Models;
using SharedKernel.Testing.Storage;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Storage;

/// <summary>
/// Proves <see cref="InMemoryBlobUriGenerator"/> against <c>IBlobUriGenerator</c>'s documented
/// contract — no consuming domain has adopted this fake yet (see <c>16.Testing/state-map.md</c>
/// T-47), so this self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class InMemoryBlobUriGeneratorTests
{
    [Fact]
    public void GeneratePresignedUploadUrl_ReturnsDeterministicInspectableUrl_AndRecordsRequest()
    {
        var generator = new InMemoryBlobUriGenerator();
        var request = new PresignedUrlRequest { Bucket = "bucket", Key = "a.txt", Expiry = TimeSpan.FromMinutes(15) };

        var result = generator.GeneratePresignedUploadUrl(request);

        Assert.True(result.IsSuccess);
        var url = result.Value.Url.ToString();
        Assert.Contains("bucket", url, StringComparison.Ordinal);
        Assert.Contains("a.txt", url, StringComparison.Ordinal);
        Assert.Contains("mode=upload", url, StringComparison.Ordinal);
        Assert.Contains(request, generator.GeneratedUploadUrls);
    }

    [Fact]
    public void GeneratePresignedDownloadUrl_ReturnsDeterministicInspectableUrl_AndRecordsRequest()
    {
        var generator = new InMemoryBlobUriGenerator();
        var request = new PresignedUrlRequest { Bucket = "bucket", Key = "a.txt", Expiry = TimeSpan.FromMinutes(5) };

        var result = generator.GeneratePresignedDownloadUrl(request);

        Assert.True(result.IsSuccess);
        Assert.Contains("mode=download", result.Value.Url.ToString(), StringComparison.Ordinal);
        Assert.Contains(request, generator.GeneratedDownloadUrls);
    }

    [Fact]
    public void GeneratePresignedUploadUrl_SameRequestTwice_IsDeterministic()
    {
        var generator = new InMemoryBlobUriGenerator();
        var request = new PresignedUrlRequest { Bucket = "bucket", Key = "a.txt", Expiry = TimeSpan.FromMinutes(15) };

        var first = generator.GeneratePresignedUploadUrl(request);
        var second = generator.GeneratePresignedUploadUrl(request);

        Assert.Equal(first.Value.Url, second.Value.Url);
        Assert.Equal(first.Value.ExpiresAt, second.Value.ExpiresAt);
    }

    [Fact]
    public void GeneratePresignedUploadUrl_ExpiryAtProviderMaximum_Succeeds()
    {
        var generator = new InMemoryBlobUriGenerator();
        var request = new PresignedUrlRequest { Bucket = "bucket", Key = "a.txt", Expiry = TimeSpan.FromDays(7) };

        var result = generator.GeneratePresignedUploadUrl(request);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void GeneratePresignedUploadUrl_ExpiryOverProviderMaximum_ReturnsExpiryTooLong()
    {
        var generator = new InMemoryBlobUriGenerator();
        var request = new PresignedUrlRequest { Bucket = "bucket", Key = "a.txt", Expiry = TimeSpan.FromDays(8) };

        var result = generator.GeneratePresignedUploadUrl(request);

        Assert.True(result.IsFailure);
        var expectedError = StorageErrors.ExpiryTooLong(request.Expiry, TimeSpan.FromDays(7));
        Assert.Equal(expectedError.Code, result.Error.Code);
        Assert.Equal(expectedError.Type, result.Error.Type);
        Assert.Empty(generator.GeneratedUploadUrls);
    }

    [Fact]
    public void GeneratePresignedDownloadUrl_ExpiryOverProviderMaximum_ReturnsExpiryTooLong()
    {
        var generator = new InMemoryBlobUriGenerator();
        var request = new PresignedUrlRequest { Bucket = "bucket", Key = "a.txt", Expiry = TimeSpan.FromDays(30) };

        var result = generator.GeneratePresignedDownloadUrl(request);

        Assert.True(result.IsFailure);
        var expectedError = StorageErrors.ExpiryTooLong(request.Expiry, TimeSpan.FromDays(7));
        Assert.Equal(expectedError.Code, result.Error.Code);
        Assert.Empty(generator.GeneratedDownloadUrls);
    }
}
