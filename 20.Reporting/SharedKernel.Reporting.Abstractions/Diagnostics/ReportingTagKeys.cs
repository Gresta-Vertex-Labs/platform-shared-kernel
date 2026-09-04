namespace SharedKernel.Reporting.Abstractions.Diagnostics;

/// <summary>
/// Single source of truth for the <see cref="System.Diagnostics.Activity.SetTag(string, object?)"/>
/// attribute-key names emitted by <see cref="ReportingActivitySource"/>.
/// </summary>
/// <remarks>
/// None of these concepts overlap an existing cross-domain
/// <c>SharedKernel.Primitives.Propagation.WellKnownTagKeys</c> entry — every key below is
/// domain-local to <c>20.Reporting</c>, per SK0022 (no raw string literal at an
/// <see cref="System.Diagnostics.Activity.SetTag(string, object?)"/> call site).
/// </remarks>
public static class ReportingTagKeys
{
    /// <summary>The tag carrying the exporting provider's format name (<c>"csv"</c>, <c>"spreadsheet"</c>, <c>"pdf"</c>).</summary>
    public const string Format = "reporting.format";

    /// <summary>The tag carrying <see cref="Models.ReportExportOutcome.RowCount"/> once export completes.</summary>
    public const string RowCount = "reporting.row_count";

    /// <summary>The tag carrying the destination bucket an export was written to.</summary>
    public const string Bucket = "reporting.destination_bucket";
}
