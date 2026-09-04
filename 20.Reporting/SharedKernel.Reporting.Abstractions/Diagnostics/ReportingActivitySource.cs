using System.Diagnostics;

namespace SharedKernel.Reporting.Abstractions.Diagnostics;

/// <summary>
/// Distributed-tracing source for report/data export operations, shared by every provider package
/// (<c>.Csv</c>, <c>.Spreadsheet</c>, <c>.Pdf</c>).
/// </summary>
/// <remarks>
/// One span per <c>IReportExporter&lt;TRow&gt;.ExportAsync</c>/<c>ExportToStreamAsync</c> call,
/// tagging <see cref="ReportingTagKeys.Format"/> up front and
/// <see cref="ReportingTagKeys.RowCount"/>/<see cref="ReportingTagKeys.Bucket"/> once the outcome is
/// known. No span emitted by any provider ever carries row content or a formatted cell value as a
/// tag — see <see cref="Exporters.IReportExporter{TRow}"/>'s PII documentation.
/// </remarks>
public static class ReportingActivitySource
{
    /// <summary>The name of this domain's <see cref="ActivitySource"/>, subscribed to by name at the OTel listener level.</summary>
    public const string Name = "SharedKernel.Reporting";

    private static readonly ActivitySource Source = new(Name);

    /// <summary>
    /// Starts the span wrapping one provider's export call, tagging <paramref name="providerFormat"/> up front.
    /// </summary>
    /// <param name="providerFormat">The exporting provider's format name (e.g. <c>"csv"</c>).</param>
    /// <returns>The started <see cref="Activity"/>, or <see langword="null"/> when no listener is sampling this source.</returns>
    public static Activity? StartExport(string providerFormat)
    {
        var activity = Source.StartActivity("ReportExporter.Export", ActivityKind.Internal);
        activity?.SetTag(ReportingTagKeys.Format, providerFormat);
        return activity;
    }
}
