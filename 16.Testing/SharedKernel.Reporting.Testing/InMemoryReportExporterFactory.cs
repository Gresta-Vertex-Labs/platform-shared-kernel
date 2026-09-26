using System.Collections.Concurrent;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;

namespace SharedKernel.Testing.Reporting;

/// <summary>
/// An in-memory <see cref="IReportExporterFactory"/> handing out one <see cref="InMemoryReportExporter{TRow}"/> per
/// format and row type, so a test can inspect what the code under test exported.
/// </summary>
/// <example>
/// <code>
/// var reports = new InMemoryReportExporterFactory();
/// var handler = new ExportOrdersHandler(reports, …);
/// await handler.Handle(new ExportOrders(Format: "xlsx"), ct);
/// reports.Exporter&lt;OrderRow&gt;(ReportFormat.Xlsx).ShouldHaveExported(rows =&gt; rows.Count == 3);
/// </code>
/// </example>
public sealed class InMemoryReportExporterFactory : IReportExporterFactory
{
    private readonly ConcurrentDictionary<(ReportFormat Format, Type Row), object> _exporters = new();
    private readonly ReportFormat[] _formats;
    private readonly IClock? _clock;

    /// <summary>Creates the fake.</summary>
    /// <param name="formats">The formats it supports; CSV, Excel and PDF when empty.</param>
    public InMemoryReportExporterFactory(params ReportFormat[] formats)
        : this(null, formats)
    {
    }

    /// <summary>Creates the fake.</summary>
    /// <param name="clock">The clock presigned links expire by.</param>
    /// <param name="formats">The formats it supports; CSV, Excel and PDF when empty.</param>
    public InMemoryReportExporterFactory(IClock? clock, params ReportFormat[] formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _clock = clock;
        _formats = formats.Length > 0 ? formats : [ReportFormat.Csv, ReportFormat.Xlsx, ReportFormat.Pdf];
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ReportFormat> Formats => _formats;

    /// <inheritdoc />
    public Result<ReportFormat> ParseFormat(string? value)
    {
        string requested = (value ?? string.Empty).Trim().TrimStart('.');
        ReportFormat? match = _formats.FirstOrDefault(f =>
            string.Equals(f.Name, requested, StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.FileExtension, "." + requested, StringComparison.OrdinalIgnoreCase)
            || string.Equals(f.ContentType, requested, StringComparison.OrdinalIgnoreCase));

        return match is not null ? match : ReportingErrors.UnsupportedFormat(value, _formats);
    }

    /// <inheritdoc />
    public IReportExporter<TRow> GetExporter<TRow>(ReportFormat format) => Exporter<TRow>(format);

    /// <summary>Returns the fake exporter for <paramref name="format"/> and <typeparamref name="TRow"/>, to inspect it.</summary>
    /// <typeparam name="TRow">The row type.</typeparam>
    /// <param name="format">A supported format.</param>
    /// <returns>The exporter; the same instance on every call.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="format"/> is not supported.</exception>
    public InMemoryReportExporter<TRow> Exporter<TRow>(ReportFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (!_formats.Contains(format))
        {
            throw new InvalidOperationException($"The fake factory does not support the format '{format.Name}'.");
        }

        return (InMemoryReportExporter<TRow>)_exporters.GetOrAdd((format, typeof(TRow)), key => new InMemoryReportExporter<TRow>(key.Format, _clock));
    }
}
