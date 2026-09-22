using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Csv.Exporters;
using SharedKernel.Reporting.Csv.Options;

namespace SharedKernel.Reporting.Csv.Extensions;

/// <summary>
/// DI extension methods for registering <c>SharedKernel.Reporting.Csv</c> services.
/// </summary>
public static class CsvReportingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CsvExportOptions"/> (validated, eagerly checked at startup via
    /// <c>ValidateOnStart()</c>), the shared <see cref="StorageStreamingWriter"/> (idempotent —
    /// safe to call alongside <c>AddSpreadsheetReportExporter</c>/<c>AddPdfReportExporter</c> in the
    /// same host), and <see cref="ICsvReportExporter{TRow}"/>/<see cref="CsvReportExporter{TRow}"/>
    /// as singletons.
    /// </summary>
    /// <typeparam name="TRow">The row type this exporter will accept.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The root <see cref="IConfiguration"/>. <see cref="CsvExportOptions"/> is bound from the
    /// <see cref="CsvExportOptions.SectionName"/> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// The caller's composition root must separately register SharedKernel storage and the stores
    /// reports are written to (<c>services.AddSharedKernelStorage().AddS3(configuration).AddStore("reports")</c>,
    /// which also provides the <c>IFileStorageFactory</c> the exporter resolves stores through) — this method does not.
    /// </remarks>
    public static IServiceCollection AddCsvReportExporter<TRow>(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<CsvExportOptions>(configuration.GetSection(CsvExportOptions.SectionName));
        services.TryAddSingleton<StorageStreamingWriter>();
        services.AddSingleton<ICsvReportExporter<TRow>, CsvReportExporter<TRow>>();

        return services;
    }
}
