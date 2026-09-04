using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Abstractions.Diagnostics;
using SharedKernel.Reporting.Abstractions.Formatting;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Reporting.Abstractions.Validation;
using SharedKernel.Reporting.Pdf.Fonts;
using SharedKernel.Reporting.Pdf.Options;

namespace SharedKernel.Reporting.Pdf.Exporters;

/// <summary>
/// PdfSharp/MigraDoc-backed implementation of <see cref="IPdfReportExporter{TRow}"/>, scoped to
/// simple tabular/statement layouts: one flat table (header row plus one row per streamed
/// <typeparamref name="TRow"/>) and an optional title, nothing else.
/// </summary>
/// <typeparam name="TRow">The row type this exporter accepts.</typeparam>
/// <remarks>
/// <para>
/// <b>SCOPE BOUNDARY (D-09), STATED IN CAPITALS.</b> THIS PROVIDER SUPPORTS EXACTLY ONE FLAT
/// STATEMENT-STYLE TABLE. MULTI-SECTION DOCUMENTS, IMAGES, CHARTS, AND HEADERS/FOOTERS BEYOND ONE
/// OPTIONAL TITLE ARE EXPLICITLY OUT OF SCOPE — THIS IS NOT A GENERAL-PURPOSE PDF AUTHORING
/// LIBRARY. MigraDoc's own table-rendering engine paginates automatically once the table exceeds
/// one page — this provider implements no pagination logic of its own.
/// </para>
/// <para>
/// <b>MEMORY MODEL (D-10), STATED IN CAPITALS.</b> MIGRADOC'S <c>Document</c>/<c>Table</c> OBJECT
/// MODEL MATERIALIZES FULLY IN MEMORY BEFORE <c>PdfDocumentRenderer</c> RUNS, AND PDFSHARP HAS NO
/// INCREMENTAL PAGE-FLUSH API EITHER — THE SAME UNDERLYING CONSTRAINT
/// <c>SharedKernel.Reporting.Spreadsheet</c> DOCUMENTS FOR CLOSEDXML. THIS IS NOT SEPARATELY
/// RE-LITIGATED AS A DEFECT BECAUSE THIS PROVIDER'S SCOPE (ABOVE) ALREADY BOUNDS IT: A SIMPLE
/// TABULAR/STATEMENT LAYOUT IS NOT THE BULK-EXPORT USE CASE <c>SharedKernel.Reporting.Csv</c>
/// EXISTS FOR. A CALLER WHO ACTUALLY NEEDS A HUGE EXPORTED TABLE SHOULD USE <c>.Csv</c> (OR
/// <c>.Spreadsheet</c>, WITH ITS OWN DOCUMENTED CAVEAT) INSTEAD OF <c>.Pdf</c>.
/// </para>
/// <para>
/// Licence attribution: both <c>PDFsharp</c> and <c>PDFsharp-MigraDoc</c> are unconditional MIT —
/// ratified in <c>20.Reporting/CLAUDE.md</c>'s licensing table. QuestPDF (revenue-gated Community
/// licence) and iText7 (AGPL) were evaluated and explicitly declined for this platform; never
/// substitute either. Rendered text uses the embedded Roboto font family (Apache License 2.0,
/// text in this package's <c>Fonts/LICENSE.txt</c>) — see
/// <see cref="Fonts.EmbeddedRobotoFontResolver"/> for why embedding, rather than reading a
/// host-installed font, is required for this provider to behave identically across the Windows
/// development environment and the Linux containers this platform's services deploy to.
/// </para>
/// </remarks>
public sealed class PdfReportExporter<TRow>(StorageStreamingWriter writer, IOptions<PdfExportOptions> options) : IPdfReportExporter<TRow>
{
    private const string ContentType = "application/pdf";
    private const string HeaderColor = "#DDDDDD";

    private readonly StorageStreamingWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly PdfExportOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

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

        using var activity = ReportingActivitySource.StartExport("pdf");

        var result = await _writer.WriteAsync(
                destination,
                ContentType,
                (stream, ct) => EncodeAsync(rows, definition, stream, ct),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsSuccess)
        {
            activity?.SetTag(ReportingTagKeys.RowCount, result.Value.RowCount);
            activity?.SetTag(ReportingTagKeys.Bucket, destination.Bucket);
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

        using var activity = ReportingActivitySource.StartExport("pdf");
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
        PdfFontResolverRegistration.EnsureRegistered();

        var orderedColumns = definition.Columns.OrderBy(c => c.Ordinal).ToArray();

        var document = new Document();
        var normalStyle = document.Styles["Normal"] ?? throw new InvalidOperationException("MigraDoc Document did not define its built-in 'Normal' style.");
        normalStyle.Font.Name = EmbeddedRobotoFontResolver.FamilyName;
        normalStyle.Font.Size = 9;

        var section = document.AddSection();
        section.PageSetup.PageFormat = _options.PageFormat;
        section.PageSetup.Orientation = _options.Orientation;

        if (!string.IsNullOrWhiteSpace(definition.Title))
        {
            var titleParagraph = section.AddParagraph(definition.Title);
            titleParagraph.Format.Font.Size = 16;
            titleParagraph.Format.Font.Bold = true;
            titleParagraph.Format.SpaceAfter = "0.5cm";
        }

        var table = section.AddTable();
        table.Borders.Width = 0.25;

        foreach (var _ in orderedColumns)
        {
            table.AddColumn();
        }

        var headerRow = table.AddRow();
        headerRow.HeadingFormat = true;
        headerRow.Format.Font.Bold = true;
        headerRow.Shading.Color = Color.Parse(HeaderColor);
        for (var i = 0; i < orderedColumns.Length; i++)
        {
            headerRow.Cells[i].AddParagraph(orderedColumns[i].Header);
        }

        long rowCount = 0;
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tableRow = table.AddRow();
            for (var i = 0; i < orderedColumns.Length; i++)
            {
                var column = orderedColumns[i];
                var raw = column.ValueSelector(row);
                var formatted = column.Formatter is not null
                    ? column.Formatter(raw, definition.Culture)
                    : ReportValueFormatting.Format(raw, definition.Culture);

                tableRow.Cells[i].AddParagraph(formatted ?? string.Empty);
            }

            rowCount++;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Synchronous/blocking by necessity — see this class's memory-model remarks above.
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        // PdfDocument.Save requires a stream that supports a readable Position (to compute xref
        // byte offsets while writing) — the pipe-backed stream StorageStreamingWriter hands every
        // provider does not support Position at all. Rendering into a local, fully-seekable buffer
        // first and copying it to the real destination afterward costs nothing beyond what this
        // provider's memory model already concedes above (the MigraDoc/PdfSharp object graph is
        // already fully materialized in memory before any byte is written), and works identically
        // regardless of what kind of Stream the caller (ExportToStreamAsync) or the delivery layer
        // (ExportAsync) supplies as the destination.
        using var buffer = new MemoryStream();
        renderer.PdfDocument.Save(buffer, closeStream: false);
        buffer.Position = 0;
        await buffer.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

        return rowCount;
    }
}
