using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Abstractions.Diagnostics;
using SharedKernel.Reporting.Abstractions.Errors;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Reporting.Abstractions.Delivery;

/// <summary>
/// The concrete mechanism behind <see cref="Exporters.IReportExporter{TRow}"/>'s Invariant 2 —
/// bytes reach object storage as a provider encodes them, with no intermediate byte-array buffering
/// at the delivery layer.
/// </summary>
/// <remarks>
/// <para>
/// Built on <see cref="System.IO.Pipelines.Pipe"/> (BCL — no third-party NuGet dependency): opens a
/// <see cref="Pipe"/>, starts <c>IFileStorage.UploadAsync</c> concurrently reading the pipe's
/// reader-side <see cref="Stream"/>, while the caller-supplied encoding delegate writes into the
/// pipe's writer-side <see cref="Stream"/>. Both tasks run under one <see cref="Task.WhenAll(Task[])"/>
/// — the writer side completes the pipe on success, or completes it with the thrown exception on
/// failure, so a faulted encoder cannot leave the reader side (and therefore the upload) hanging
/// forever waiting for more bytes that will never arrive.
/// </para>
/// <para>
/// This makes Invariant 2 concrete rather than aspirational: the delivery layer itself never
/// buffers the full output as a byte array before upload, <em>regardless of what a given provider's
/// own row-to-bytes encoding step does internally</em> — see <c>SharedKernel.Reporting.Spreadsheet</c>/
/// <c>.Pdf</c>'s own XML docs for the honest, provider-specific memory-model caveat their underlying
/// third-party libraries carry.
/// </para>
/// <para>
/// Not part of the public consumer-facing API surface application code calls directly — every
/// provider's <c>ExportAsync</c> composes this type internally. It is <see langword="public"/>
/// (rather than <see langword="internal"/>) purely so provider packages in other assemblies
/// (<c>.Csv</c>, <c>.Spreadsheet</c>, <c>.Pdf</c>) can construct/inject and unit test it directly.
/// </para>
/// </remarks>
public sealed class StorageStreamingWriter(
    IFileStorage fileStorage,
    ILogger<StorageStreamingWriter> logger,
    IBlobUriGenerator? blobUriGenerator = null)
{
    private readonly IFileStorage _fileStorage = fileStorage ?? throw new ArgumentNullException(nameof(fileStorage));
    private readonly ILogger<StorageStreamingWriter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Runs <paramref name="encodeAsync"/> and an <c>IFileStorage.UploadAsync</c> call concurrently
    /// against a shared <see cref="Pipe"/>, then optionally presigns a download URL.
    /// </summary>
    /// <param name="destination">Where the encoded output is written.</param>
    /// <param name="contentType">The MIME type of the encoded output (e.g. <c>"text/csv"</c>).</param>
    /// <param name="encodeAsync">
    /// The provider's encoder. Writes the encoded output into the given <see cref="Stream"/> (the
    /// pipe's writer side) and returns the number of rows encoded. Never disposes the stream —
    /// this method owns the pipe's lifetime.
    /// </param>
    /// <param name="cancellationToken">
    /// Token observed by both the encoder and the upload — cancelling stops consuming the row
    /// source promptly rather than draining it.
    /// </param>
    /// <returns>
    /// The <see cref="ReportExportOutcome"/> on success, or a failed <see cref="Result{T}"/> via
    /// <c>StorageErrors</c> (upload failure) or <see cref="ReportingErrors"/> (presign requested
    /// with no <see cref="IBlobUriGenerator"/> available). A thrown exception from
    /// <paramref name="encodeAsync"/> propagates as a faulted <see cref="Task"/> rather than
    /// becoming a <see cref="Result{T}"/> failure — encoding faults are programming/data errors,
    /// not expected outcomes.
    /// </returns>
    public async Task<Result<ReportExportOutcome>> WriteAsync(
        ReportDestination destination,
        string contentType,
        Func<Stream, CancellationToken, Task<long>> encodeAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(contentType);
        ArgumentNullException.ThrowIfNull(encodeAsync);

        var pipe = new Pipe();
        var readerStream = pipe.Reader.AsStream();

        var uploadRequest = new FileUploadRequest
        {
            Bucket = destination.Bucket,
            Key = destination.Key,
            Content = readerStream,
            ContentType = contentType,
            Metadata = destination.Metadata,
        };

        ReportingLog.ExportStarted(_logger, destination.Bucket, destination.Key);
        var stopwatch = Stopwatch.StartNew();

        var uploadTask = _fileStorage.UploadAsync(uploadRequest, cancellationToken);
        var encodeTask = RunEncodeAsync(pipe.Writer, encodeAsync, cancellationToken);

        try
        {
            await Task.WhenAll(uploadTask, encodeTask).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            ReportingLog.ExportFailed(_logger, ex, destination.Bucket, destination.Key);
            throw;
        }

        var rowCount = encodeTask.Result;
        var uploadResult = uploadTask.Result;
        stopwatch.Stop();

        if (uploadResult.IsFailure)
        {
            ReportingLog.ExportFailed(
                _logger,
                new InvalidOperationException(uploadResult.Error.Message),
                destination.Bucket,
                destination.Key);
            return Result<ReportExportOutcome>.Failure(uploadResult.Error);
        }

        PresignedUrl? downloadUrl = null;
        if (destination.PresignedDownloadUrlExpiry is { } expiry)
        {
            if (blobUriGenerator is null)
            {
                return Result<ReportExportOutcome>.Failure(ReportingErrors.PresignedUrlUnavailable());
            }

            var presignResult = blobUriGenerator.GeneratePresignedDownloadUrl(new PresignedUrlRequest
            {
                Bucket = destination.Bucket,
                Key = destination.Key,
                Expiry = expiry,
            });

            if (presignResult.IsFailure)
            {
                return Result<ReportExportOutcome>.Failure(presignResult.Error);
            }

            downloadUrl = presignResult.Value;
        }

        ReportingLog.ExportCompleted(_logger, destination.Bucket, destination.Key, rowCount, stopwatch.Elapsed.TotalMilliseconds);

        return Result<ReportExportOutcome>.Success(new ReportExportOutcome
        {
            StoredFile = uploadResult.Value,
            DownloadUrl = downloadUrl,
            RowCount = rowCount,
        });
    }

    private static async Task<long> RunEncodeAsync(
        PipeWriter writer,
        Func<Stream, CancellationToken, Task<long>> encodeAsync,
        CancellationToken cancellationToken)
    {
        var writerStream = writer.AsStream();
        try
        {
            var rowCount = await encodeAsync(writerStream, cancellationToken).ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
            return rowCount;
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(ex).ConfigureAwait(false);
            throw;
        }
    }
}
