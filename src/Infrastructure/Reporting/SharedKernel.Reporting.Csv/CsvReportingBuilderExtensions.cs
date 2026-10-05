using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Reporting.Csv;

namespace SharedKernel.Reporting;

/// <summary>Adds CSV to SharedKernel reporting.</summary>
public static class CsvReportingBuilderExtensions
{
    /// <summary>
    /// Adds the CSV format for every row type: <see cref="ICsvReportExporter{TRow}"/>, the <c>"csv"</c> exporter of
    /// <see cref="IReportExporterFactory"/>, and <see cref="CsvExportOptions"/> bound from
    /// <c>SharedKernel:Reporting:Csv</c> and validated at startup.
    /// </summary>
    /// <param name="builder">The builder from <c>services.AddSharedKernelReporting()</c>.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The builder.</returns>
    public static IReportingBuilder AddCsv(this IReportingBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        builder.Services.AddValidatedOptions<CsvExportOptions>(configuration);
        builder.Services.TryAddSingleton(typeof(ICsvReportExporter<>), typeof(CsvReportExporter<>));
        return builder.AddExporter(ReportFormat.Csv, typeof(CsvReportExporter<>));
    }
}
