using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Reporting.Internal;

/// <summary>One format and its open generic exporter type, recorded by <c>AddExporter</c>.</summary>
internal sealed record ReportExporterRegistration(ReportFormat Format, Type ExporterType);

/// <summary>Resolves the keyed exporter registered for a format.</summary>
internal sealed class ReportExporterFactory(IServiceProvider services, IEnumerable<ReportExporterRegistration> registrations)
    : IReportExporterFactory
{
    private readonly ReportFormat[] _formats = registrations.Select(r => r.Format).Distinct().ToArray();

    public IReadOnlyCollection<ReportFormat> Formats => _formats;

    public Result<ReportFormat> ParseFormat(string? value)
    {
        string requested = (value ?? string.Empty).Trim();
        int parameters = requested.IndexOf(';', StringComparison.Ordinal);
        if (parameters >= 0)
        {
            requested = requested[..parameters].TrimEnd();
        }

        string name = requested.StartsWith('.') ? requested[1..] : requested;

        foreach (ReportFormat format in _formats)
        {
            if (string.Equals(format.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(format.FileExtension, "." + name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(format.ContentType, requested, StringComparison.OrdinalIgnoreCase))
            {
                return format;
            }
        }

        return ReportingErrors.UnsupportedFormat(value, _formats);
    }

    public IReportExporter<TRow> GetExporter<TRow>(ReportFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        if (!_formats.Contains(format))
        {
            string registered = _formats.Length == 0 ? "none" : string.Join(", ", _formats.Select(f => f.Name));
            throw new InvalidOperationException(
                $"No report exporter is registered for the format '{format.Name}' (registered: {registered}). "
                + "Add it on services.AddSharedKernelReporting(), e.g. .AddCsv(configuration), .AddSpreadsheet(configuration) or .AddPdf(configuration).");
        }

        return services.GetRequiredKeyedService<IReportExporter<TRow>>(format.Name);
    }
}
