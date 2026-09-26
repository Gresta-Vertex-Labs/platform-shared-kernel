using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;

namespace SharedKernel.Reporting.Pdf;

/// <summary>How PDF exports are laid out. Bound from <c>SharedKernel:Reporting:Pdf</c>; every setting is optional.</summary>
/// <example>
/// <code>
/// "SharedKernel": { "Reporting": { "Pdf": { "PaperSize": "Letter", "Landscape": true, "MaxRows": 50000 } } }
/// </code>
/// </example>
public sealed class PdfExportOptions : ISectionBoundOptions
{
    /// <summary>Gets the configuration section: <c>SharedKernel:Reporting:Pdf</c>.</summary>
    public static string SectionName => "SharedKernel:Reporting:Pdf";

    /// <summary>Gets or sets the paper size. Defaults to <see cref="PdfPaperSize.A4"/>.</summary>
    [EnumDataType(typeof(PdfPaperSize))]
    public PdfPaperSize PaperSize { get; set; } = PdfPaperSize.A4;

    /// <summary>
    /// Gets or sets whether pages are landscape — the usual choice for wide tables. Defaults to
    /// <see langword="false"/> (portrait).
    /// </summary>
    public bool Landscape { get; set; }

    /// <summary>Gets or sets the margin on every side, in millimetres. Defaults to 15.</summary>
    [Range(0d, 100d)]
    public double MarginMillimeters { get; set; } = 15;

    /// <summary>Gets or sets the table's font size in points; the title is larger. Defaults to 9.</summary>
    [Range(5d, 24d)]
    public double FontSize { get; set; } = 9;

    /// <summary>Gets or sets whether every page shows "page / pages" at the bottom. Defaults to <see langword="true"/>.</summary>
    public bool ShowPageNumbers { get; set; } = true;

    /// <summary>Gets or sets whether every other row is lightly shaded. Defaults to <see langword="true"/>.</summary>
    public bool AlternateRowShading { get; set; } = true;

    /// <summary>
    /// Gets or sets the most rows an export may have; more fail with <c>reporting.row_limit_exceeded</c> and store
    /// nothing. The whole document is built in memory before it is rendered, so this bounds memory and time. Defaults
    /// to 10,000.
    /// </summary>
    [Range(1, 1_000_000)]
    public int MaxRows { get; set; } = 10_000;
}

/// <summary>A paper size for PDF exports.</summary>
public enum PdfPaperSize
{
    /// <summary>A4, 210 × 297 mm.</summary>
    A4 = 0,

    /// <summary>A3, 297 × 420 mm.</summary>
    A3 = 1,

    /// <summary>A5, 148 × 210 mm.</summary>
    A5 = 2,

    /// <summary>US Letter, 8.5 × 11 in.</summary>
    Letter = 3,

    /// <summary>US Legal, 8.5 × 14 in.</summary>
    Legal = 4,
}
