using System.ComponentModel.DataAnnotations;
using MigraDoc.DocumentObjectModel;

namespace SharedKernel.Reporting.Pdf.Options;

/// <summary>
/// Configuration for <see cref="Exporters.PdfReportExporter{TRow}"/>, registered by
/// <see cref="Extensions.PdfReportingServiceCollectionExtensions.AddPdfReportExporter{TRow}"/>.
/// </summary>
public sealed class PdfExportOptions
{
    /// <summary>The configuration section name this options class binds to.</summary>
    public const string SectionName = "SharedKernel:Reporting:Pdf";

    /// <summary>The page size. Defaults to <see cref="PageFormat.A4"/>.</summary>
    [EnumDataType(typeof(PageFormat))]
    public PageFormat PageFormat { get; set; } = PageFormat.A4;

    /// <summary>The page orientation. Defaults to <see cref="Orientation.Portrait"/>.</summary>
    [EnumDataType(typeof(Orientation))]
    public Orientation Orientation { get; set; } = Orientation.Portrait;
}
