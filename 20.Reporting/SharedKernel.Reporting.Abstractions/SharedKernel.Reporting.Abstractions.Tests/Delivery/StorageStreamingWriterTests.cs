using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.Abstractions.Errors;
using SharedKernel.Storage.Abstractions.Models;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Abstractions.Tests.Delivery;

public sealed class StorageStreamingWriterTests
{
    private static readonly ReportDestination Destination = new() { Bucket = "reports", Key = "export.csv" };

    [Fact]
    public async Task WriteAsync_EncoderReadsConcurrentlyWithUpload_FirstByteReachesUploadBeforeEncodingFinishes()
    {
        // Proves the two tasks genuinely run concurrently — the reader side observes bytes before
        // the writer side has finished producing all of them — rather than "encode fully, then
        // upload" sequential composition disguised behind one method call.
        var recordingStorage = new TimingRecordingFileStorage();
        var writer = new StorageStreamingWriter(recordingStorage, NullLogger<StorageStreamingWriter>.Instance);

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
        var fileStorage = new InMemoryFileStorage();
        var writer = new StorageStreamingWriter(fileStorage, NullLogger<StorageStreamingWriter>.Instance);

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
        var fileStorage = new InMemoryFileStorage();
        var writer = new StorageStreamingWriter(fileStorage, NullLogger<StorageStreamingWriter>.Instance);

        var result = await writer.WriteAsync(
            Destination,
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
        result.Value.StoredFile.Bucket.Should().Be(Destination.Bucket);
        result.Value.StoredFile.Key.Should().Be(Destination.Key);
        fileStorage.WasUploaded(Destination.Bucket, Destination.Key).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_RowCount_MatchesActualEncoderCount_ComputedWithoutSecondPass()
    {
        // Proves ReportExportOutcome.RowCount reflects whatever the encoder actually counted while
        // streaming — StorageStreamingWriter neither recomputes nor re-derives it from a second
        // pass over anything; it simply carries the encoder's own tally through untouched.
        var fileStorage = new InMemoryFileStorage();
        var writer = new StorageStreamingWriter(fileStorage, NullLogger<StorageStreamingWriter>.Instance);
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
        var fileStorage = new InMemoryFileStorage { SimulateFailure = true };
        var writer = new StorageStreamingWriter(fileStorage, NullLogger<StorageStreamingWriter>.Instance);

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
    }

    [Fact]
    public async Task WriteAsync_PresignRequestedWithNoGenerator_ReturnsFailure()
    {
        var fileStorage = new InMemoryFileStorage();
        var writer = new StorageStreamingWriter(fileStorage, NullLogger<StorageStreamingWriter>.Instance);
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

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("reporting.presigned_url_unavailable");
    }

    [Fact]
    public async Task WriteAsync_PresignRequestedWithGenerator_PopulatesDownloadUrl()
    {
        var fileStorage = new InMemoryFileStorage();
        var blobUriGenerator = new InMemoryBlobUriGenerator();
        var writer = new StorageStreamingWriter(fileStorage, NullLogger<StorageStreamingWriter>.Instance, blobUriGenerator);
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
    }

    /// <summary>
    /// An <see cref="IFileStorage"/> double that reads the upload stream in small chunks, recording
    /// when the first chunk was observed versus when reading finally finished — timing evidence
    /// that the reader (upload) side is actively pulling data while the writer (encoder) side is
    /// still producing it, not merely after the fact.
    /// </summary>
    private sealed class TimingRecordingFileStorage : IFileStorage
    {
        private readonly System.Diagnostics.Stopwatch _stopwatch = new();

        public TimingRecordingFileStorage() => _stopwatch.Start();

        public TaskCompletionSource FirstReadObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public long FirstReadElapsedTicks { get; private set; } = long.MaxValue;

        public long FinalReadElapsedTicks { get; private set; }

        public async Task<Result<FileReference>> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            var first = true;
            int read;
            while ((read = await request.Content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (first)
                {
                    FirstReadElapsedTicks = _stopwatch.ElapsedTicks;
                    first = false;
                    FirstReadObserved.TrySetResult();
                }

                FinalReadElapsedTicks = _stopwatch.ElapsedTicks;
            }

            FinalReadElapsedTicks = _stopwatch.ElapsedTicks;

            return Result<FileReference>.Success(new FileReference { Bucket = request.Bucket, Key = request.Key });
        }

        public Task<Result<FileDownload>> DownloadAsync(string bucket, string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> DeleteAsync(string bucket, string key, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());

        public Task<Result<bool>> ExistsAsync(string bucket, string key, CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Success(false));

        public Task<Result<FileMetadata>> GetMetadataAsync(string bucket, string key, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<FileReference>> CopyAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<FileDeleteOutcome>>> DeleteManyAsync(string bucket, IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<FileMetadata> ListAsync(string bucket, string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<Result> CheckHealthAsync(string bucket, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }
}
