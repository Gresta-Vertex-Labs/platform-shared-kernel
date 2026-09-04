namespace SharedKernel.Reporting.Csv.Options;

/// <summary>
/// Configuration for <see cref="Exporters.CsvReportExporter{TRow}"/>, registered by
/// <see cref="Extensions.CsvReportingServiceCollectionExtensions.AddCsvReportExporter{TRow}"/>.
/// </summary>
public sealed class CsvExportOptions
{
    /// <summary>The configuration section name this options class binds to.</summary>
    public const string SectionName = "SharedKernel:Reporting:Csv";

    /// <summary>
    /// Whether the encoded output is preceded by a UTF-8 byte-order mark. Defaults to
    /// <see langword="true"/> — RFC 4180 itself is silent on BOM, so this is a deliberate platform
    /// default: the target audience (statements/regulatory extracts opened by a business user in
    /// Excel) benefits from the BOM far more often than it is harmed by it.
    /// </summary>
    public bool IncludeUtf8Bom { get; set; } = true;

    /// <summary>The field delimiter. Defaults to <c>,</c> per RFC 4180.</summary>
    public char Delimiter { get; set; } = ',';
}
