using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Reporting.Pdf;

namespace SharedKernel.Reporting;

/// <summary>Adds tabular PDF to SharedKernel reporting.</summary>
public static class PdfReportingBuilderExtensions
{
    /// <summary>
    /// Adds the PDF format for every row type: <see cref="IPdfReportExporter{TRow}"/>, the <c>"pdf"</c> exporter of
    /// <see cref="IReportExporterFactory"/>, and <see cref="PdfExportOptions"/> bound from
    /// <c>SharedKernel:Reporting:Pdf</c> and validated at startup.
    /// </summary>
    /// <param name="builder">The builder from <c>services.AddSharedKernelReporting()</c>.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The builder.</returns>
    public static IReportingBuilder AddPdf(this IReportingBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddValidatedOptions<PdfExportOptions>(configuration);
        builder.Services.TryAddSingleton(typeof(IPdfReportExporter<>), typeof(PdfReportExporter<>));
        return builder.AddExporter(ReportFormat.Pdf, typeof(PdfReportExporter<>));
    }
}
