using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Storage;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Abstractions.Tests.Delivery;

public sealed class StorageStreamingWriterTests
{
    private static readonly TenantId TenantA = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));

    private static readonly ReportDestination Destination = new() { Store = "reports", Key = "export.csv" };

    [Fact]
    public async Task WriteAsync_EncoderReadsConcurrentlyWithUpload_FirstByteReachesUploadBeforeEncodingFinishes()
    {
        // Proves the two tasks genuinely run concurrently — the reader side observes bytes before
        // the writer side has finished producing all of them — rather than "encode fully, then
        // upload" sequential composition disguised behind one method call.
        var recordingStorage = new TimingRecordingFileStorage(new InMemoryFileStorage("reports"));
        var factory = InMemoryStorage.CreateFactory(b => b.AddStore(new FileStoreRegistration(
            "reports",
            tenantScoped: false,
            _ => recordingStorage,
            (_, _) => Task.FromResult(Result.Success()))));
        var writer = new StorageStreamingWriter(factory, NullLogger<StorageStreamingWriter>.Instance);

        const int rowsToWrite = 20;

        var result = await writer.WriteAsync(
            Destination,
            "text/csv",
            async (stream, ct) =>
            {
                for (var i = 0; i < rowsToWrite; i++)
                {
                    var line = new string('x', 8192) + "\n";
                    var bytes = System.Text.Encoding.UTF8.GetBytes(line);
                    await stream.WriteAsync(bytes, ct);
                    await stream.FlushAsync(ct);

                    if (i == 0)
                    {
                        // Give the reader side a chance to observe the first flushed chunk before
                        // the encoder keeps going.
                        await recordingStorage.FirstReadObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
                    }
                }

                return (long)rowsToWrite;
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        recordingStorage.FirstReadObserved.Task.IsCompletedSuccessfully.Should().BeTrue();
        recordingStorage.FirstReadElapsedTicks.Should().BeLessThan(recordingStorage.FinalReadElapsedTicks);
    }

    [Fact]
    public async Task WriteAsync_EncoderThrows_SurfacesAsFaultedTaskRatherThanHanging()
    {
        var fileStorage = new InMemoryFileStorage("reports");
        var writer = new StorageStreamingWriter(InMemoryStorage.CreateFactory(fileStorage), NullLogger<StorageStreamingWriter>.Instance);

        var act = async () => await writer.WriteAsync(
            Destination,
            "text/csv",
            (_, _) => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task WriteAsync_Success_ReturnsOutcomeWithRowCountAndStoredFile()
    {
        var fileStorage = new InMemoryFileStorage("reports");
        var writer = new StorageStreamingWriter(InMemoryStorage.CreateFactory(fileStorage), NullLogger<StorageStreamingWriter>.Instance);
        var destination = Destination with { Metadata = new Dictionary<string, string> { ["report"] = "sales" } };

        var result = await writer.WriteAsync(
            destination,
            "text/csv",
            async (stream, ct) =>
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes("a,b,c\n1,2,3\n");
                await stream.WriteAsync(bytes, ct);
                return 1L;
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowCount.Should().Be(1);
        result.Value.StoredFile.Store.Should().Be(Destination.Store);
        result.Value.StoredFile.TenantId.Should().BeNull();
        result.Value.StoredFile.Key.Should().Be(Destination.Key);
        result.Value.StoredFile.ETag.Should().NotBeNullOrEmpty();
        fileStorage.WasUploaded(Destination.Key).Should().BeTrue();
        fileStorage.GetContent(Destination.Key).Should().Equal(System.Text.Encoding.UTF8.GetBytes("a,b,c\n1,2,3\n"));

        var properties = (await fileStorage.GetPropertiesAsync(Destination.Key)).Value;
        properties.ContentType.Should().Be("text/csv");
        properties.Metadata.Should().ContainKey("report").WhoseValue.Should().Be("sales");
    }

    [Fact]
    public async Task WriteAsync_TenantDestination_WritesThroughTheTenantsView()
    {
        var fileStorage = new InMemoryFileStorage("documents");
        var factory = InMemoryStorage.CreateFactory(b => b.AddInMemoryTenantStore(fileStorage));
        var writer = new StorageStreamingWriter(factory, NullLogger<StorageStreamingWriter>.Instance);
        var destination = new ReportDestination { Store = "documents", TenantId = TenantA, Key = "exports/export.csv" };

        var result = await writer.WriteAsync(
            destination,
            "text/csv",
            async (stream, ct) =>
            {
                await stream.WriteAsync("x"u8.ToArray(), ct);
                return 1L;
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.StoredFile.Store.Should().Be("documents");
        result.Value.StoredFile.TenantId.Should().Be(TenantA);
        result.Value.StoredFile.Key.Should().Be("exports/export.csv");
        fileStorage.Keys.Should().Equal(InMemoryFileStorage.TenantKey(TenantA, "exports/export.csv"));
        (await factory.Open(result.Value.StoredFile).ExistsAsync(result.Value.StoredFile.Key)).Value.Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_RowCount_MatchesActualEncoderCount_ComputedWithoutSecondPass()
    {
        // Proves ReportExportOutcome.RowCount reflects whatever the encoder actually counted while
        // streaming — StorageStreamingWriter neither recomputes nor re-derives it from a second
        // pass over anything; it simply carries the encoder's own tally through untouched.
        var fileStorage = new InMemoryFileStorage("reports");
        var writer = new StorageStreamingWriter(InMemoryStorage.CreateFactory(fileStorage), NullLogger<StorageStreamingWriter>.Instance);
        const long expectedRowCount = 4321;

        var result = await writer.WriteAsync(
            Destination,
            "text/csv",
            async (stream, ct) =>
            {
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("payload"), ct);
                return expectedRowCount;
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RowCount.Should().Be(expectedRowCount);
    }

    [Fact]
    public async Task WriteAsync_UploadFails_ReturnsFailedResult()
    {
        var fileStorage = new InMemoryFileStorage("reports") { SimulateFailure = true };
        var writer = new StorageStreamingWriter(InMemoryStorage.CreateFactory(fileStorage), NullLogger<StorageStreamingWriter>.Instance);

        var result = await writer.WriteAsync(
            Destination,
            "text/csv",
            async (stream, ct) =>
            {
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("x"), ct);
                return 1L;
            },
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(StorageErrorCodes.Unavailable);
    }

    [Fact]
    public async Task WriteAsync_UploadRejectedBeforeReading_ReturnsFailureInsteadOfBlockingTheEncoder()
    {
        // The registry rejects an invalid key before the provider reads a byte. The encoder writes far
        // more than the pipe buffers, so without releasing the reader side it would block forever.
        var fileStorage = new InMemoryFileStorage("reports");
        var writer = new StorageStreamingWriter(InMemoryStorage.CreateFactory(fileStorage), NullLogger<StorageStreamingWriter>.Instance);
        var destination = Destination with { Key = "../escape.csv" };

        var result = await writer.WriteAsync(
            destination,
            "text/csv",
            async (stream, ct) =>
            {
                var chunk = new byte[64 * 1024];
                for (var i = 0; i < 64; i++)
                {
                    await stream.WriteAsync(chunk, ct);
                    await stream.FlushAsync(ct);
                }

                return 64L;
            },
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(StorageErrorCodes.InvalidKey);
        fileStorage.Keys.Should().BeEmpty();
    }

    [Fact]
    public async Task WriteAsync_PresignRequested_PopulatesDownloadUrlFromTheSameStore()
    {
        var fileStorage = new InMemoryFileStorage("reports");
        var writer = new StorageStreamingWriter(InMemoryStorage.CreateFactory(fileStorage), NullLogger<StorageStreamingWriter>.Instance);
        var destination = Destination with { PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(5) };

        var result = await writer.WriteAsync(
            destination,
            "text/csv",
            async (stream, ct) =>
            {
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("x"), ct);
                return 1L;
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.DownloadUrl.Should().NotBeNull();
        result.Value.DownloadUrl!.Method.Should().Be("GET");
        fileStorage.IssuedDownloadUrls.Should().ContainSingle().Which.Key.Should().Be(Destination.Key);
    }

    [Fact]
    public async Task WriteAsync_PresignExpiryBeyondTheStoresMaximum_ReturnsExpiryTooLong()
    {
        var fileStorage = new InMemoryFileStorage("reports", new InMemoryFileStorageOptions { MaxPresignExpiry = TimeSpan.FromMinutes(10) });
        var writer = new StorageStreamingWriter(InMemoryStorage.CreateFactory(fileStorage), NullLogger<StorageStreamingWriter>.Instance);
        var destination = Destination with { PresignedDownloadUrlExpiry = TimeSpan.FromHours(2) };

        var result = await writer.WriteAsync(
            destination,
            "text/csv",
            async (stream, ct) =>
            {
                await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("x"), ct);
                return 1L;
            },
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(StorageErrorCodes.ExpiryTooLong);
    }

    [Fact]
    public async Task WriteAsync_UnknownStore_ThrowsConfigurationError()
    {
        var writer = new StorageStreamingWriter(
            InMemoryStorage.CreateFactory(new InMemoryFileStorage("reports")),
            NullLogger<StorageStreamingWriter>.Instance);

        var act = async () => await writer.WriteAsync(
            Destination with { Store = "unregistered" },
            "text/csv",
            (_, _) => Task.FromResult(0L),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unregistered*");
    }

    [Fact]
    public async Task WriteAsync_TenantOnASharedStore_ThrowsConfigurationError()
    {
        var writer = new StorageStreamingWriter(
            InMemoryStorage.CreateFactory(new InMemoryFileStorage("reports")),
            NullLogger<StorageStreamingWriter>.Instance);

        var act = async () => await writer.WriteAsync(
            Destination with { TenantId = TenantA },
            "text/csv",
            (_, _) => Task.FromResult(0L),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_NullFactory_Throws()
    {
        var act = () => new StorageStreamingWriter(null!, NullLogger<StorageStreamingWriter>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// An <see cref="IFileStorage"/> provider store that reads the upload stream in small chunks,
    /// recording when the first chunk was observed versus when reading finally finished — timing
    /// evidence that the reader (upload) side is actively pulling data while the writer (encoder)
    /// side is still producing it, not merely after the fact. Every other member delegates to an
    /// <see cref="InMemoryFileStorage"/>.
    /// </summary>
    private sealed class TimingRecordingFileStorage(InMemoryFileStorage inner) : IFileStorage
    {
        private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

        public TaskCompletionSource FirstReadObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public long FirstReadElapsedTicks { get; private set; } = long.MaxValue;

        public long FinalReadElapsedTicks { get; private set; }

        public string StoreName => inner.StoreName;

        public TenantId? TenantId => null;

        public async Task<Result<FileReference>> UploadAsync(string key, Stream content, FileUploadOptions? options = null, CancellationToken cancellationToken = default)
        {
            using var received = new MemoryStream();
            var buffer = new byte[4096];
            var first = true;
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (first)
                {
                    FirstReadElapsedTicks = _stopwatch.ElapsedTicks;
                    first = false;
                    FirstReadObserved.TrySetResult();
                }

                received.Write(buffer, 0, read);
                FinalReadElapsedTicks = _stopwatch.ElapsedTicks;
            }

            FinalReadElapsedTicks = _stopwatch.ElapsedTicks;
            received.Position = 0;
            return await inner.UploadAsync(key, received, options, cancellationToken);
        }

        public Task<Result<FileDownload>> DownloadAsync(string key, FileDownloadOptions? options = null, CancellationToken cancellationToken = default) => inner.DownloadAsync(key, options, cancellationToken);

        public Task<Result<FileProperties>> GetPropertiesAsync(string key, CancellationToken cancellationToken = default) => inner.GetPropertiesAsync(key, cancellationToken);

        public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default) => inner.ExistsAsync(key, cancellationToken);

        public Task<Result> DeleteAsync(string key, FileDeleteOptions? options = null, CancellationToken cancellationToken = default) => inner.DeleteAsync(key, options, cancellationToken);

        public Task<Result<BatchDeleteResult>> DeleteManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) => inner.DeleteManyAsync(keys, cancellationToken);

        public Task<Result<FileReference>> CopyAsync(string sourceKey, string destinationKey, FileCopyOptions? options = null, CancellationToken cancellationToken = default) => inner.CopyAsync(sourceKey, destinationKey, options, cancellationToken);

        public Task<Result<FileReference>> CopyToAsync(string sourceKey, IFileStorage destination, string destinationKey, FileCopyOptions? options = null, CancellationToken cancellationToken = default) => inner.CopyToAsync(sourceKey, destination, destinationKey, options, cancellationToken);

        public IAsyncEnumerable<FileListItem> ListAsync(string prefix = "", CancellationToken cancellationToken = default) => inner.ListAsync(prefix, cancellationToken);

        public Task<Result<FileListPage>> ListPageAsync(FileListRequest request, CancellationToken cancellationToken = default) => inner.ListPageAsync(request, cancellationToken);

        public Task<Result<PresignedRequest>> CreateDownloadUrlAsync(string key, PresignedDownloadOptions options, CancellationToken cancellationToken = default) => inner.CreateDownloadUrlAsync(key, options, cancellationToken);

        public Task<Result<PresignedRequest>> CreateUploadUrlAsync(string key, PresignedUploadOptions options, CancellationToken cancellationToken = default) => inner.CreateUploadUrlAsync(key, options, cancellationToken);

        public Task<Result<PresignedPost>> CreateUploadFormAsync(string key, PresignedPostOptions options, CancellationToken cancellationToken = default) => inner.CreateUploadFormAsync(key, options, cancellationToken);

        public Task<Result<MultipartUpload>> StartMultipartUploadAsync(string key, MultipartUploadOptions? options = null, CancellationToken cancellationToken = default) => inner.StartMultipartUploadAsync(key, options, cancellationToken);

        public Task<Result<PresignedRequest>> CreateUploadPartUrlAsync(MultipartUpload upload, int partNumber, TimeSpan expiry, CancellationToken cancellationToken = default) => inner.CreateUploadPartUrlAsync(upload, partNumber, expiry, cancellationToken);

        public Task<Result<FileReference>> CompleteMultipartUploadAsync(MultipartUpload upload, IReadOnlyCollection<UploadedPart> parts, WriteCondition? condition = null, CancellationToken cancellationToken = default) => inner.CompleteMultipartUploadAsync(upload, parts, condition, cancellationToken);

        public Task<Result> AbortMultipartUploadAsync(MultipartUpload upload, CancellationToken cancellationToken = default) => inner.AbortMultipartUploadAsync(upload, cancellationToken);
    }
}
