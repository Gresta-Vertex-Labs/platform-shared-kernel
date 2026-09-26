using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Reporting;

namespace SharedKernel.Testing.Reporting;

/// <summary>Replaces SharedKernel reporting with in-memory fakes in a test host.</summary>
public static class InMemoryReportingServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InMemoryReportExporterFactory"/> as the <see cref="IReportExporterFactory"/> and
    /// <see cref="InMemoryHtmlToPdfConverter"/> as the <see cref="IHtmlToPdfConverter"/>, replacing real ones. Resolve
    /// the concrete fakes to inspect them.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddInMemoryReporting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IReportExporterFactory>();
        services.RemoveAll<IHtmlToPdfConverter>();
        services.TryAddSingleton(sp => new InMemoryReportExporterFactory(sp.GetService<IClock>()));
        services.TryAddSingleton<InMemoryHtmlToPdfConverter>();
        services.AddSingleton<IReportExporterFactory>(sp => sp.GetRequiredService<InMemoryReportExporterFactory>());
        services.AddSingleton<IHtmlToPdfConverter>(sp => sp.GetRequiredService<InMemoryHtmlToPdfConverter>());
        return services;
    }
}
