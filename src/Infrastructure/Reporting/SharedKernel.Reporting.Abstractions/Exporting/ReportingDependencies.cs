using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Internal;
using SharedKernel.Storage;

namespace SharedKernel.Reporting;

/// <summary>
/// The services every exporter and converter needs — storage delivery, logging, tracing and metrics — passed to the
/// <see cref="ReportExporterBase{TRow}"/> and <see cref="HtmlToPdfConverterBase"/> constructors.
/// </summary>
/// <remarks>
/// Registered by <c>AddSharedKernelReporting()</c>. A custom exporter takes it as a constructor parameter and hands it
/// to its base class. Storage is optional: without <c>AddSharedKernelStorage()</c>, exporting to a stream works and
/// exporting to a store throws <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class ReportingDependencies
{
    private readonly IFileStorageFactory? _storageFactory;
    private readonly ILogger _logger;

    /// <summary>Creates the dependencies.</summary>
    /// <param name="loggerFactory">The logger factory; no logging when <see langword="null"/>.</param>
    /// <param name="storageFactory">The storage factory; exporting to a store throws when <see langword="null"/>.</param>
    public ReportingDependencies(ILoggerFactory? loggerFactory = null, IFileStorageFactory? storageFactory = null)
    {
        _storageFactory = storageFactory;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger(ReportingLog.CategoryName);
    }

    /// <summary>Runs <paramref name="write"/> into a pipe while uploading its other end to the destination store.</summary>
    internal async Task<Result<StoredContent>> StoreAsync(
        string operation,
        ReportFormat format,
        ReportDestination destination,
        Func<Stream, CancellationToken, Task<Result<long>>> write,
        bool countsRows,
        CancellationToken cancellationToken)
    {
        IFileStorage store = OpenStore(destination);

        return await ObserveAsync(
                operation,
                format,
                destination.Store,
                countsRows,
                () => StoreCoreAsync(store, format, destination, write, cancellationToken),
                stored => (stored.RowCount, stored.SizeBytes),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Runs <paramref name="write"/> against <paramref name="destination"/>, counting the bytes.</summary>
    internal Task<Result<WrittenContent>> WriteAsync(
        string operation,
        ReportFormat format,
        Stream destination,
        Func<Stream, CancellationToken, Task<Result<long>>> write,
        bool countsRows,
        CancellationToken cancellationToken) =>
        ObserveAsync<WrittenContent>(
            operation,
            format,
            store: null,
            countsRows,
            async () =>
            {
                var counter = new CountingWriteStream(destination);
                Result<long> written = await write(counter, cancellationToken).ConfigureAwait(false);
                if (written.IsFailure)
                {
                    return written.Error;
                }

                await counter.FlushAsync(cancellationToken).ConfigureAwait(false);
                return new WrittenContent(written.Value, counter.BytesWritten);
            },
            written => (written.RowCount, written.SizeBytes),
            cancellationToken);

    private static string BuildContentDisposition(string fileName)
    {
        var ascii = new StringBuilder(fileName.Length);
        foreach (char c in fileName)
        {
            ascii.Append(c is >= ' ' and <= '~' and not '"' and not '\\' ? c : '_');
        }

        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
    }

    private static async Task<Result<FileReference>> UploadAsync(
        IFileStorage store,
        string key,
        PipeReader reader,
        FileUploadOptions options,
        CancellationTokenSource writerCancellation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await store.UploadAsync(key, reader.AsStream(), options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // An upload that stopped reading early (a failed condition, an invalid key) must not leave the writer
            // blocked on a full pipe, nor let it drain the rest of the row source for nothing.
            await reader.CompleteAsync().ConfigureAwait(false);
            await writerCancellation.CancelAsync().ConfigureAwait(false);
        }
    }

    private static async Task<Result<long>> EncodeAsync(
        PipeWriter writer,
        CountingWriteStream counter,
        Func<Stream, CancellationToken, Task<Result<long>>> write,
        CancellationToken cancellationToken)
    {
        try
        {
            Result<long> written = await write(counter, cancellationToken).ConfigureAwait(false);
            if (written.IsFailure)
            {
                // Faulting the pipe aborts the upload, so a report that failed half-way is never stored.
                await writer.CompleteAsync(new ReportAbortedException(written.Error.Code)).ConfigureAwait(false);
                return written;
            }

            await counter.FlushAsync(cancellationToken).ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
            return written;
        }
        catch (Exception exception)
        {
            await writer.CompleteAsync(exception).ConfigureAwait(false);
            throw;
        }
    }

    private IFileStorage OpenStore(ReportDestination destination)
    {
        if (_storageFactory is null)
        {
            throw new InvalidOperationException(
                "Exporting to a store needs SharedKernel storage: register it with services.AddSharedKernelStorage() and add the store. "
                + "To write to a stream instead, call ExportToStreamAsync / ConvertToStreamAsync.");
        }

        return _storageFactory.Open(new FileReference
        {
            Store = destination.Store,
            TenantId = destination.TenantId,
            Key = destination.Key,
        });
    }

    private async Task<Result<StoredContent>> StoreCoreAsync(
        IFileStorage store,
        ReportFormat format,
        ReportDestination destination,
        Func<Stream, CancellationToken, Task<Result<long>>> write,
        CancellationToken cancellationToken)
    {
        var uploadOptions = new FileUploadOptions
        {
            ContentType = format.ContentType,
            ContentDisposition = destination.DownloadFileName is { } fileName ? BuildContentDisposition(fileName) : null,
            Metadata = destination.Metadata,
            Condition = destination.Condition,
        };

        var pipe = new Pipe();
        using var writerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var counter = new CountingWriteStream(pipe.Writer.AsStream(leaveOpen: true), writerCancellation.Token);

        Task<Result<FileReference>> upload = UploadAsync(store, destination.Key, pipe.Reader, uploadOptions, writerCancellation, cancellationToken);
        Task<Result<long>> encode = EncodeAsync(pipe.Writer, counter, write, writerCancellation.Token);
        await Task.WhenAll(upload, encode).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // The writer's own fault is the root cause; the upload only saw the pipe it broke.
        if (encode.IsFaulted)
        {
            ExceptionDispatchInfo.Throw(encode.Exception.InnerException ?? encode.Exception);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (encode.IsCompletedSuccessfully && encode.Result.IsFailure)
        {
            return encode.Result.Error;
        }

        Result<FileReference> uploaded = await upload.ConfigureAwait(false);
        if (uploaded.IsFailure)
        {
            return uploaded.Error;
        }

        long rowCount = await encode.ConfigureAwait(false) is { IsSuccess: true } encoded ? encoded.Value : 0;

        PresignedRequest? downloadUrl = null;
        if (destination.PresignedDownloadUrlExpiry is { } expiry)
        {
            Result<PresignedRequest> presigned = await store
                .CreateDownloadUrlAsync(destination.Key, new PresignedDownloadOptions { Expiry = expiry }, cancellationToken)
                .ConfigureAwait(false);

            if (presigned.IsFailure)
            {
                ReportingLog.PresignFailed(_logger, destination.Store, presigned.Error.Code);
                return presigned.Error;
            }

            downloadUrl = presigned.Value;
        }

        return new StoredContent(uploaded.Value, downloadUrl, rowCount, counter.BytesWritten);
    }

    private async Task<Result<T>> ObserveAsync<T>(
        string operation,
        ReportFormat format,
        string? store,
        bool countsRows,
        Func<Task<Result<T>>> body,
        Func<T, (long RowCount, long SizeBytes)> measure,
        CancellationToken cancellationToken)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        using Activity? activity = ReportingTelemetry.Start(operation, format, store);
        ReportingLog.Started(_logger, operation, format.Name, store ?? "(stream)");

        try
        {
            Result<T> result = await body().ConfigureAwait(false);
            if (result.IsFailure)
            {
                ReportingTelemetry.Failed(activity, operation, format, startTimestamp, result.Error.Code);
                ReportingLog.Failed(_logger, operation, format.Name, result.Error.Code);
                return result;
            }

            (long rowCount, long sizeBytes) = measure(result.Value);
            ReportingTelemetry.Succeeded(activity, operation, format, startTimestamp, countsRows ? rowCount : null, sizeBytes);

            double durationMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            if (countsRows)
            {
                ReportingLog.Completed(_logger, operation, format.Name, rowCount, sizeBytes, durationMs);
            }
            else
            {
                ReportingLog.Converted(_logger, sizeBytes, durationMs);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ReportingTelemetry.Cancelled(activity, operation, format, startTimestamp);
            ReportingLog.Cancelled(_logger, operation, format.Name);
            throw;
        }
        catch (Exception exception)
        {
            ReportingTelemetry.Failed(activity, operation, format, startTimestamp, exception.GetType().FullName ?? exception.GetType().Name);
            ReportingLog.Faulted(_logger, exception, operation, format.Name);
            throw;
        }
    }
}

/// <summary>What <see cref="ReportingDependencies"/> stored.</summary>
internal readonly record struct StoredContent(FileReference StoredFile, PresignedRequest? DownloadUrl, long RowCount, long SizeBytes);

/// <summary>What <see cref="ReportingDependencies"/> wrote to a stream.</summary>
internal readonly record struct WrittenContent(long RowCount, long SizeBytes);

/// <summary>Faults the upload pipe when a writer returned a failure, so the partial report is never stored.</summary>
internal sealed class ReportAbortedException(string errorCode)
    : Exception($"The report was not completed: {errorCode}.");
