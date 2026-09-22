using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Abstractions.Diagnostics;
using SharedKernel.Reporting.Abstractions.Formatting;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Reporting.Abstractions.Validation;
using SharedKernel.Reporting.Csv.Options;

namespace SharedKernel.Reporting.Csv.Exporters;

/// <summary>
/// RFC 4180 CSV implementation of <see cref="ICsvReportExporter{TRow}"/> — hand-written, zero
/// third-party NuGet dependency, and the only one of this domain's three providers that is
/// genuinely constant-memory end to end: encoding happens directly against the destination
/// <see cref="Stream"/> via <see cref="StreamWriter"/>, one row at a time, as the
/// <see cref="IAsyncEnumerable{T}"/> source is enumerated.
/// </summary>
/// <typeparam name="TRow">The row type this exporter accepts.</typeparam>
/// <remarks>
/// <para>
/// <b>Escaping rule (RFC 4180):</b> a field is quoted when it contains the configured
/// <see cref="CsvExportOptions.Delimiter"/>, a double quote, <c>\r</c>, or <c>\n</c>; an embedded
/// double quote is escaped by doubling it. This single rule applies uniformly to every formatted
/// string regardless of source type — a culture whose decimal separator is <c>,</c> (e.g.
/// <c>de-DE</c>) produces a numeric field that already contains a comma, and the general
/// "contains the delimiter → quote it" rule handles it correctly with no numeric-specific
/// special case anywhere in this encoder.
/// </para>
/// </remarks>
public sealed class CsvReportExporter<TRow>(StorageStreamingWriter writer, IOptions<CsvExportOptions> options) : ICsvReportExporter<TRow>
{
    private const string ContentType = "text/csv";
    private const string LineTerminator = "\r\n";

    private readonly StorageStreamingWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly CsvExportOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<Result<ReportExportOutcome>> ExportAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        ReportDestination destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var validation = ReportExportPreconditions.ValidateDefinitionAndDestination(definition, destination);
        if (validation.IsFailure)
        {
            return Result<ReportExportOutcome>.Failure(validation.Error);
        }

        using var activity = ReportingActivitySource.StartExport("csv");

        var result = await _writer.WriteAsync(
                destination,
                ContentType,
                (stream, ct) => EncodeAsync(rows, definition, stream, ct),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            activity?.SetTag(ReportingTagKeys.RowCount, result.Value.RowCount);
            activity?.SetTag(ReportingTagKeys.Store, destination.Store);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<Result> ExportToStreamAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(destination);

        var validation = ReportExportPreconditions.ValidateDefinition(definition);
        if (validation.IsFailure)
        {
            return validation;
        }

        using var activity = ReportingActivitySource.StartExport("csv");
        var rowCount = await EncodeAsync(rows, definition, destination, cancellationToken).ConfigureAwait(false);
        activity?.SetTag(ReportingTagKeys.RowCount, rowCount);

        return Result.Success();
    }

    private async Task<long> EncodeAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var orderedColumns = definition.Columns.OrderBy(c => c.Ordinal).ToArray();
        var encoding = _options.IncludeUtf8Bom ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true) : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var streamWriter = new StreamWriter(destination, encoding, bufferSize: 4096, leaveOpen: true) { NewLine = LineTerminator };
        await using (streamWriter)
        {
            await WriteRowAsync(streamWriter, orderedColumns.Select(c => c.Header), cancellationToken).ConfigureAwait(false);

            long rowCount = 0;
            await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fields = new string?[orderedColumns.Length];
                for (var i = 0; i < orderedColumns.Length; i++)
                {
                    fields[i] = FormatValue(orderedColumns[i], row, definition.Culture);
                }

                await WriteRowAsync(streamWriter, fields, cancellationToken).ConfigureAwait(false);
                rowCount++;
            }

            await streamWriter.FlushAsync(cancellationToken).ConfigureAwait(false);
            return rowCount;
        }
    }

    private static string? FormatValue(ReportColumn<TRow> column, TRow row, CultureInfo culture)
    {
        var raw = column.ValueSelector(row);
        return column.Formatter is not null ? column.Formatter(raw, culture) : ReportValueFormatting.Format(raw, culture);
    }

    private async Task WriteRowAsync(StreamWriter streamWriter, IEnumerable<string?> fields, CancellationToken cancellationToken)
    {
        var first = true;
        foreach (var field in fields)
        {
            if (!first)
            {
                await streamWriter.WriteAsync(_options.Delimiter).ConfigureAwait(false);
            }

            first = false;
            await streamWriter.WriteAsync(Escape(field).AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await streamWriter.WriteAsync(LineTerminator.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private string Escape(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        var needsQuoting = field.IndexOf(_options.Delimiter) >= 0
            || field.IndexOf('"') >= 0
            || field.IndexOf('\r') >= 0
            || field.IndexOf('\n') >= 0;

        return needsQuoting
            ? string.Concat("\"", field.Replace("\"", "\"\"", StringComparison.Ordinal), "\"")
            : field;
    }
}
