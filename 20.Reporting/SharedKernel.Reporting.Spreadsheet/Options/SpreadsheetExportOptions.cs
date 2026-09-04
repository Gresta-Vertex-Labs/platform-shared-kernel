namespace SharedKernel.Reporting.Spreadsheet.Options;

/// <summary>
/// Configuration for <see cref="Exporters.SpreadsheetReportExporter{TRow}"/>, registered by
/// <see cref="Extensions.SpreadsheetReportingServiceCollectionExtensions.AddSpreadsheetReportExporter{TRow}"/>.
/// </summary>
public sealed class SpreadsheetExportOptions
{
    /// <summary>The configuration section name this options class binds to.</summary>
    public const string SectionName = "SharedKernel:Reporting:Spreadsheet";

    /// <summary>
    /// The worksheet name used when <c>ReportDefinition&lt;TRow&gt;.Title</c> is <see langword="null"/>.
    /// Defaults to <c>"Report"</c>.
    /// </summary>
    public string DefaultSheetName { get; set; } = "Report";

    /// <summary>Whether the header row is rendered bold. Defaults to <see langword="true"/>.</summary>
    public bool BoldHeaderRow { get; set; } = true;
}
