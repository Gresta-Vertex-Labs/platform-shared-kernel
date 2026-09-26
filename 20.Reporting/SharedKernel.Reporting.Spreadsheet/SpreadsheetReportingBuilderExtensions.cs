using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Reporting.Spreadsheet;

namespace SharedKernel.Reporting;

/// <summary>Adds Excel (.xlsx) to SharedKernel reporting.</summary>
public static class SpreadsheetReportingBuilderExtensions
{
    /// <summary>
    /// Adds the Excel format for every row type: <see cref="ISpreadsheetReportExporter{TRow}"/>, the <c>"xlsx"</c>
    /// exporter of <see cref="IReportExporterFactory"/>, and <see cref="SpreadsheetExportOptions"/> bound from
    /// <c>SharedKernel:Reporting:Spreadsheet</c> and validated at startup.
    /// </summary>
    /// <param name="builder">The builder from <c>services.AddSharedKernelReporting()</c>.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The builder.</returns>
    public static IReportingBuilder AddSpreadsheet(this IReportingBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddValidatedOptions<SpreadsheetExportOptions>(configuration);
        builder.Services.TryAddSingleton(typeof(ISpreadsheetReportExporter<>), typeof(SpreadsheetReportExporter<>));
        return builder.AddExporter(ReportFormat.Xlsx, typeof(SpreadsheetReportExporter<>));
    }
}
