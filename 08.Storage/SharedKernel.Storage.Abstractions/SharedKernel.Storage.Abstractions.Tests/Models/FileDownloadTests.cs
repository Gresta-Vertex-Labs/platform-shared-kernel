using FluentAssertions;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Storage.Abstractions.Tests.Models;

/// <summary>
/// T-02: <see cref="FileDownload"/> — sealed class (not a record; reference-equality by design)
/// implementing <see cref="IAsyncDisposable"/>, owning the provider network stream.
/// </summary>
public sealed class FileDownloadTests
{
    [Fact]
    public void FileDownload_Implements_IAsyncDisposable()
    {
        typeof(IAsyncDisposable).IsAssignableFrom(typeof(FileDownload)).Should().BeTrue(
            "FileDownload wraps a caller-disposed provider network stream");
    }

    [Fact]
    public async Task DisposeAsync_Disposes_UnderlyingContentStream()
    {
        var trackingStream = new DisposeTrackingStream();

        var download = new FileDownload
        {
            Content = trackingStream,
            ContentType = "application/octet-stream",
            ContentLength = 0,
            Metadata = new Dictionary<string, string>(),
        };

        await download.DisposeAsync();

        trackingStream.WasDisposed.Should().BeTrue("DisposeAsync must release the provider network stream");
    }

    [Fact]
    public void FileDownload_IsSealedClass()
    {
        typeof(FileDownload).IsSealed.Should().BeTrue();
        typeof(FileDownload).IsClass.Should().BeTrue();
    }

    /// <summary>A no-op stream that records whether it was disposed, for disposal-contract assertions.</summary>
    private sealed class DisposeTrackingStream : MemoryStream
    {
        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
