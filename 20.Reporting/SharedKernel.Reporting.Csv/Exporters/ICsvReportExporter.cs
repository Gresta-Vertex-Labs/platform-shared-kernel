using SharedKernel.Reporting.Abstractions.Exporters;

namespace SharedKernel.Reporting.Csv.Exporters;

/// <summary>
/// Provider-exclusive marker interface for the CSV <see cref="IReportExporter{TRow}"/>
/// implementation. Declared only in this package — mirroring <c>09.Search</c>/<c>10.Intelligence</c>'s
/// established compile-time provider-exclusivity pattern. Injecting this interface against a
/// composition root that never registered <c>SharedKernel.Reporting.Csv</c> fails to compile, never
/// a runtime format-string check.
/// </summary>
/// <typeparam name="TRow">The row type this exporter accepts.</typeparam>
public interface ICsvReportExporter<TRow> : IReportExporter<TRow>;
