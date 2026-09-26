using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Internal;

namespace SharedKernel.Reporting;

/// <summary>
/// The base of every <see cref="IReportExporter{TRow}"/>: validation, storage delivery, tracing, metrics and logging
/// are done here, so an exporter only encodes rows into bytes.
/// </summary>
/// <typeparam name="TRow">The row type.</typeparam>
/// <example>
/// A custom format, registered with <c>AddSharedKernelReporting().AddExporter(JsonLines, typeof(JsonLinesExporter&lt;&gt;))</c>:
/// <code>
/// internal sealed class JsonLinesExporter&lt;TRow&gt;(ReportingDependencies dependencies) : ReportExporterBase&lt;TRow&gt;(dependencies)
/// {
///     public override ReportFormat Format =&gt; JsonLines;
///
///     protected override async Task&lt;Result&lt;long&gt;&gt; EncodeAsync(
///         IAsyncEnumerable&lt;TRow&gt; rows, ReportDefinition&lt;TRow&gt; definition, Stream destination, CancellationToken cancellationToken)
///     {
///         long count = 0;
///         await foreach (TRow row in rows.WithCancellation(cancellationToken))
///         {
///             // write one line per row to destination
///             count++;
///         }
///
///         return count;
///     }
/// }
/// </code>
/// </example>
public abstract class ReportExporterBase<TRow> : IReportExporter<TRow>
{
    private readonly ReportingDependencies _dependencies;

    /// <summary>Initializes the exporter.</summary>
    /// <param name="dependencies">The shared reporting services, from dependency injection.</param>
    protected ReportExporterBase(ReportingDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    /// <inheritdoc />
    public abstract ReportFormat Format { get; }

    /// <inheritdoc />
    public async Task<Result<ReportExportOutcome>> ExportAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        ReportDestination destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(destination);

        Result valid = Validate(definition);
        if (valid.IsSuccess)
        {
            valid = ReportRequestValidator.ValidateDestination(destination);
        }

        if (valid.IsFailure)
        {
            return valid.Error;
        }

        ReportFormat format = Format;
        Result<StoredContent> stored = await _dependencies
            .StoreAsync(
                ReportingTelemetry.ExportOperation,
                format,
                destination,
                (stream, ct) => EncodeAsync(rows, definition, stream, ct),
                countsRows: true,
                cancellationToken)
            .ConfigureAwait(false);

        return stored.IsFailure
            ? stored.Error
            : new ReportExportOutcome
            {
                StoredFile = stored.Value.StoredFile,
                DownloadUrl = stored.Value.DownloadUrl,
                Format = format,
                RowCount = stored.Value.RowCount,
                SizeBytes = stored.Value.SizeBytes,
            };
    }

    /// <inheritdoc />
    public async Task<Result<ReportStreamOutcome>> ExportToStreamAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(destination);

        Result valid = Validate(definition);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        ReportFormat format = Format;
        Result<WrittenContent> written = await _dependencies
            .WriteAsync(
                ReportingTelemetry.ExportOperation,
                format,
                destination,
                (stream, ct) => EncodeAsync(rows, definition, stream, ct),
                countsRows: true,
                cancellationToken)
            .ConfigureAwait(false);

        return written.IsFailure
            ? written.Error
            : new ReportStreamOutcome { Format = format, RowCount = written.Value.RowCount, SizeBytes = written.Value.SizeBytes };
    }

    /// <summary>
    /// Checks format-specific rules on top of the common ones (at least one column, every column complete). Called
    /// before any byte is written.
    /// </summary>
    /// <param name="definition">The definition, already checked by the common rules.</param>
    /// <returns>Success, or a failure such as <see cref="ReportingErrors.InvalidDefinition(string)"/>.</returns>
    protected virtual Result ValidateDefinition(ReportDefinition<TRow> definition) => Result.Success();

    /// <summary>
    /// Encodes <paramref name="rows"/> into <paramref name="destination"/>. Enumerate the rows once, honour
    /// <paramref name="cancellationToken"/>, and never dispose <paramref name="destination"/>.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="definition">The validated definition.</param>
    /// <param name="destination">
    /// The stream to write to: write-only and not seekable. For a store it feeds the upload directly.
    /// </param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <returns>
    /// The number of rows written, or a failure such as <see cref="ReportingErrors.RowLimitExceeded"/>; on a failure
    /// nothing is stored.
    /// </returns>
    protected abstract Task<Result<long>> EncodeAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken);

    private Result Validate(ReportDefinition<TRow> definition)
    {
        Result common = ReportRequestValidator.ValidateDefinition(definition);
        return common.IsFailure ? common : ValidateDefinition(definition);
    }
}
