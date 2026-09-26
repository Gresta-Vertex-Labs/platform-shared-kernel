using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Reporting.Internal;

namespace SharedKernel.Reporting;

/// <summary>Configures reporting: the formats and the HTML-to-PDF converter a service uses.</summary>
/// <remarks>Returned by <see cref="ReportingServiceCollectionExtensions.AddSharedKernelReporting"/>; each provider package adds an extension method.</remarks>
public interface IReportingBuilder
{
    /// <summary>Gets the service collection reporting is registered in.</summary>
    IServiceCollection Services { get; }
}

/// <summary>Registers SharedKernel reporting.</summary>
public static class ReportingServiceCollectionExtensions
{
    /// <summary>
    /// Registers reporting — <see cref="IReportExporterFactory"/> and the shared <see cref="ReportingDependencies"/> —
    /// and returns the builder the formats are added to. Safe to call more than once.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddSharedKernelStorage().AddS3(builder.Configuration).AddStore("reports");
    /// builder.Services.AddSharedKernelReporting()
    ///     .AddCsv(builder.Configuration)
    ///     .AddSpreadsheet(builder.Configuration)
    ///     .AddPdf(builder.Configuration)
    ///     .AddGotenberg(builder.Configuration);   // IHtmlToPdfConverter
    /// builder.WithReportingTelemetry();
    /// </code>
    /// </example>
    /// <param name="services">The service collection.</param>
    /// <returns>The reporting builder.</returns>
    public static IReportingBuilder AddSharedKernelReporting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ReportingDependencies>();
        services.TryAddSingleton<IReportExporterFactory, ReportExporterFactory>();
        return new ReportingBuilder(services);
    }

    /// <summary>
    /// Registers an exporter for <paramref name="format"/>: an open generic type such as
    /// <c>typeof(JsonLinesExporter&lt;&gt;)</c> implementing <see cref="IReportExporter{TRow}"/>, usually by deriving from
    /// <see cref="ReportExporterBase{TRow}"/>. It serves every row type, through <see cref="IReportExporterFactory"/>
    /// and as the keyed service <c>[FromKeyedServices("{format}")] IReportExporter&lt;TRow&gt;</c>. Registering the same
    /// type again is a no-op.
    /// </summary>
    /// <param name="builder">The reporting builder.</param>
    /// <param name="format">The format the exporter writes.</param>
    /// <param name="exporterType">The open generic exporter type.</param>
    /// <returns>The reporting builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="exporterType"/> is not an open generic <see cref="IReportExporter{TRow}"/> with one type parameter.</exception>
    /// <exception cref="InvalidOperationException">Another exporter type is already registered for <paramref name="format"/>.</exception>
    public static IReportingBuilder AddExporter(this IReportingBuilder builder, ReportFormat format, Type exporterType)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(exporterType);

        if (!exporterType.IsClass || exporterType.IsAbstract || !exporterType.IsGenericTypeDefinition
            || exporterType.GetGenericArguments().Length != 1
            || !exporterType.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IReportExporter<>)))
        {
            throw new ArgumentException(
                $"{exporterType} must be a non-abstract open generic class with one type parameter implementing IReportExporter<TRow>, e.g. typeof(MyExporter<>).",
                nameof(exporterType));
        }

        ReportExporterRegistration? existing = builder.Services
            .Where(d => d.ServiceType == typeof(ReportExporterRegistration) && !d.IsKeyedService)
            .Select(d => d.ImplementationInstance as ReportExporterRegistration)
            .FirstOrDefault(r => r?.Format == format);

        if (existing is not null)
        {
            return existing.ExporterType == exporterType
                ? builder
                : throw new InvalidOperationException(
                    $"The report format '{format.Name}' already has the exporter {existing.ExporterType}; it cannot also use {exporterType}.");
        }

        builder.Services.AddSingleton(new ReportExporterRegistration(format, exporterType));
        builder.Services.AddKeyedSingleton(typeof(IReportExporter<>), format.Name, exporterType);
        return builder;
    }

    /// <summary>
    /// Registers <typeparamref name="TConverter"/> as the <see cref="IHtmlToPdfConverter"/>, replacing any other.
    /// Provider packages call this; <c>AddGotenberg</c> is the built-in one.
    /// </summary>
    /// <typeparam name="TConverter">The converter, usually derived from <see cref="HtmlToPdfConverterBase"/>.</typeparam>
    /// <param name="builder">The reporting builder.</param>
    /// <returns>The reporting builder.</returns>
    public static IReportingBuilder AddHtmlToPdfConverter<TConverter>(this IReportingBuilder builder)
        where TConverter : class, IHtmlToPdfConverter
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.RemoveAll<IHtmlToPdfConverter>();
        builder.Services.AddSingleton<IHtmlToPdfConverter, TConverter>();
        return builder;
    }

    private sealed class ReportingBuilder(IServiceCollection services) : IReportingBuilder
    {
        public IServiceCollection Services { get; } = services;
    }
}
