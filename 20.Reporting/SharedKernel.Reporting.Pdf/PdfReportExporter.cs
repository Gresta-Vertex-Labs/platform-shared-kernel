using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Reporting.Pdf;

/// <summary>
/// Tabular PDF on PDFsharp/MigraDoc: an optional title and one table, paginated by MigraDoc with the header row repeated
/// on every page.
/// </summary>
/// <remarks>
/// <para>
/// <b>Memory.</b> MigraDoc builds the whole document before PDFsharp renders it, so memory grows with the row count;
/// <see cref="PdfExportOptions.MaxRows"/> bounds it. Bulk exports belong in CSV or Excel. Free-form documents
/// (invoices, letters) belong in HTML, rendered by an <see cref="IHtmlToPdfConverter"/>.
/// </para>
/// <para>
/// <b>Fonts.</b> Text renders in the embedded Roboto family, identical on every host. It covers Latin (including
/// Turkish), Greek and Cyrillic.
/// </para>
/// </remarks>
internal sealed class PdfReportExporter<TRow>(ReportingDependencies dependencies, IOptions<PdfExportOptions> options)
    : ReportExporterBase<TRow>(dependencies), IPdfReportExporter<TRow>
{
    private readonly PdfExportOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    public override ReportFormat Format => ReportFormat.Pdf;

    protected override async Task<Result<long>> EncodeAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var document = new PdfTableDocument<TRow>(definition, _options);

        long rowCount = 0;
        await foreach (TRow row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (rowCount == _options.MaxRows)
            {
                return ReportingErrors.RowLimitExceeded(Format, _options.MaxRows);
            }

            document.AddRow(row);
            rowCount++;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await document.SaveAsync(destination, cancellationToken).ConfigureAwait(false);
        return rowCount;
    }
}
