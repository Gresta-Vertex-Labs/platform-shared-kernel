using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Reporting.Abstractions.Delivery;
using SharedKernel.Reporting.Pdf.Exporters;
using SharedKernel.Reporting.Pdf.Options;

namespace SharedKernel.Reporting.Pdf.Extensions;

/// <summary>
/// DI extension methods for registering <c>SharedKernel.Reporting.Pdf</c> services.
/// </summary>
public static class PdfReportingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PdfExportOptions"/> (validated, eagerly checked at startup via
    /// <c>ValidateOnStart()</c>), the shared <see cref="StorageStreamingWriter"/> (idempotent —
    /// safe to call alongside <c>AddCsvReportExporter</c>/<c>AddSpreadsheetReportExporter</c> in the
    /// same host), and <see cref="IPdfReportExporter{TRow}"/>/<see cref="PdfReportExporter{TRow}"/>
    /// as singletons.
    /// </summary>
    /// <typeparam name="TRow">The row type this exporter will accept.</typeparam>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">
    /// The root <see cref="IConfiguration"/>. <see cref="PdfExportOptions"/> is bound from the
    /// <see cref="PdfExportOptions.SectionName"/> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// The caller's composition root must separately register <c>IFileStorage</c> (and, if
    /// presigned download URLs are needed, <c>IBlobUriGenerator</c>) from a
    /// <c>SharedKernel.Storage.*</c> provider — this method does not register either.
    /// </remarks>
    public static IServiceCollection AddPdfReportExporter<TRow>(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<PdfExportOptions>(configuration.GetSection(PdfExportOptions.SectionName));
        services.TryAddSingleton<StorageStreamingWriter>();
        services.AddSingleton<IPdfReportExporter<TRow>, PdfReportExporter<TRow>>();

        return services;
    }
}
