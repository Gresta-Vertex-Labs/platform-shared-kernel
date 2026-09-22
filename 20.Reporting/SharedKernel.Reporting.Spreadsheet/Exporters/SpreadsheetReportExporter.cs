using ClosedXML.Excel;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Abstractions.Diagnostics;
using SharedKernel.Reporting.Abstractions.Formatting;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Reporting.Abstractions.Validation;
using SharedKernel.Reporting.Spreadsheet.Options;

namespace SharedKernel.Reporting.Spreadsheet.Exporters;

/// <summary>
/// ClosedXML-backed <c>.xlsx</c> implementation of <see cref="ISpreadsheetReportExporter{TRow}"/>.
/// Writes the header row and one row per streamed <typeparamref name="TRow"/> directly into
/// worksheet cells as the <see cref="IAsyncEnumerable{T}"/> source is enumerated — never buffers
/// rows into an intermediate <see cref="List{T}"/> first.
/// </summary>
/// <typeparam name="TRow">The row type this exporter accepts.</typeparam>
/// <remarks>
/// <para>
/// <b>IMPORTANT — MEMORY MODEL, VERIFIED, NOT ASSUMED (WO-077 Design, D-08).</b> CLOSEDXML ITSELF
/// EXPOSES NO INCREMENTAL/STREAMING WRITE PATH. <c>XLWorkbook.SaveAs</c> BUILDS THE COMPLETE
/// IN-MEMORY WORKBOOK OBJECT GRAPH AND ONLY SERIALIZES IT TO THE DESTINATION STREAM AT <c>SaveAs</c>
/// TIME — A DOCUMENTED REAL-WORLD CASE SAW A 32&#160;MB <c>.xlsx</c> OUTPUT CONSUME 1+&#160;GB OF
/// PROCESS MEMORY (<c>ClosedXML/ClosedXML#1180</c>). THIS PROVIDER IS THEREFORE <b>NOT</b>
/// MEMORY-BOUNDED, END TO END, DESPITE NEVER MATERIALIZING <typeparamref name="TRow"/> INTO A
/// <see cref="List{T}"/> ITSELF — THE WORKBOOK'S OWN CELL OBJECT GRAPH IS O(ROWS &#215; COLUMNS)
/// REGARDLESS. THIS IS A PERMANENT, ACCEPTED CHARACTERISTIC OF THE CLOSEDXML DEPENDENCY, NOT A
/// DEFECT AWAITING A FIX. FOR A GENUINELY LARGE ROW COUNT, USE
/// <c>SharedKernel.Reporting.Csv</c> INSTEAD.
/// </para>
/// <para>
/// <c>workbook.SaveAs(stream)</c> is a synchronous, blocking call — ClosedXML has no async
/// <c>SaveAs</c> overload in any MIT-licensed version this platform pins. Called directly inside
/// this exporter's async method body; accepted because report generation is expected to run inside
/// a background/worker context (a <c>19.Scheduling</c> job or <c>17.Workflows</c> activity), never
/// on a request thread.
/// </para>
/// </remarks>
public sealed class SpreadsheetReportExporter<TRow>(StorageStreamingWriter writer, IOptions<SpreadsheetExportOptions> options)
    : ISpreadsheetReportExporter<TRow>
{
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const int MaxSheetNameLength = 31;
    private static readonly char[] InvalidSheetNameChars = ['\\', '/', '?', '*', '[', ']', ':'];

    private readonly StorageStreamingWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly SpreadsheetExportOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

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

        using var activity = ReportingActivitySource.StartExport("spreadsheet");

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

        using var activity = ReportingActivitySource.StartExport("spreadsheet");
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

        using var workbook = new XLWorkbook();
        var sheetName = SanitizeSheetName(definition.Title ?? _options.DefaultSheetName);
        var worksheet = workbook.Worksheets.Add(sheetName);

        for (var i = 0; i < orderedColumns.Length; i++)
        {
            var cell = worksheet.Cell(1, i + 1);
            cell.Value = orderedColumns[i].Header;
            if (_options.BoldHeaderRow)
            {
                cell.Style.Font.Bold = true;
            }
        }

        long rowCount = 0;
        var rowIndex = 2;
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            for (var i = 0; i < orderedColumns.Length; i++)
            {
                var column = orderedColumns[i];
                var raw = column.ValueSelector(row);
                var formatted = column.Formatter is not null
                    ? column.Formatter(raw, definition.Culture)
                    : ReportValueFormatting.Format(raw, definition.Culture);

                worksheet.Cell(rowIndex, i + 1).Value = formatted ?? string.Empty;
            }

            rowIndex++;
            rowCount++;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Synchronous/blocking by necessity — see this class's memory-model remarks above.
        workbook.SaveAs(destination);

        return rowCount;
    }

    private static string SanitizeSheetName(string candidate)
    {
        var sanitized = new string(candidate.Select(c => InvalidSheetNameChars.Contains(c) ? '_' : c).ToArray()).Trim();
        if (sanitized.Length == 0)
        {
            sanitized = "Report";
        }

        return sanitized.Length > MaxSheetNameLength ? sanitized[..MaxSheetNameLength] : sanitized;
    }
}
