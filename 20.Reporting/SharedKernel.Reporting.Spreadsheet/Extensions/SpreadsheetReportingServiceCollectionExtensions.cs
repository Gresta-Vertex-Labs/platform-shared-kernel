using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Spreadsheet.Exporters;
using SharedKernel.Reporting.Spreadsheet.Options;

namespace SharedKernel.Reporting.Spreadsheet.Extensions;

/// <summary>
/// DI extension methods for registering <c>SharedKernel.Reporting.Spreadsheet</c> services.
/// </summary>
public static class SpreadsheetReportingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SpreadsheetExportOptions"/> (validated, eagerly checked at startup via
    /// <c>ValidateOnStart()</c>), the shared <see cref="StorageStreamingWriter"/> (idempotent —
    /// safe to call alongside <c>AddCsvReportExporter</c>/<c>AddPdfReportExporter</c> in the same
    /// host), and <see cref="ISpreadsheetReportExporter{TRow}"/>/<see cref="SpreadsheetReportExporter{TRow}"/>
    /// as singletons.
    /// </summary>
    /// <typeparam name="TRow">The row type this exporter will accept.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The root <see cref="IConfiguration"/>. <see cref="SpreadsheetExportOptions"/> is bound from
    /// the <see cref="SpreadsheetExportOptions.SectionName"/> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// The caller's composition root must separately register <c>IFileStorage</c> (and, if
    /// presigned download URLs are needed, <c>IBlobUriGenerator</c>) from a
    /// <c>SharedKernel.Storage.*</c> provider — this method does not register either.
    /// </remarks>
    public static IServiceCollection AddSpreadsheetReportExporter<TRow>(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<SpreadsheetExportOptions>(configuration.GetSection(SpreadsheetExportOptions.SectionName));
        services.TryAddSingleton<StorageStreamingWriter>();
        services.AddSingleton<ISpreadsheetReportExporter<TRow>, SpreadsheetReportExporter<TRow>>();

        return services;
    }
}
