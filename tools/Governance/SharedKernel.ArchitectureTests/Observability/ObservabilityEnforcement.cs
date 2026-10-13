namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>
/// Applies the platform-wide observability assertions: a finding fails the test.
/// </summary>
/// <remarks>
/// There is one behaviour. The per-domain table is written for the test log, then one aggregated exception is thrown when the report
/// has a finding. <see cref="ObservabilityReport.Unverifiable"/> entries are listed and never fail.
/// </remarks>
public static class ObservabilityEnforcement
{
    /// <summary>
    /// Writes the per-domain table, then fails when the report has a finding.
    /// </summary>
    /// <param name="report">The report to apply.</param>
    /// <param name="output">Where the table goes; <see cref="Console.Out"/> when <see langword="null"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown once, aggregating every finding.</exception>
    public static void Apply(ObservabilityReport report, TextWriter? output = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        var writer = output ?? Console.Out;
        writer.WriteLine("Observability report");
        writer.WriteLine(report.Render());

        if (!report.IsClean)
        {
            throw new InvalidOperationException(
                $"Observability assertions found {report.Findings.Count} violation(s):" + Environment.NewLine + report.Render() + Environment.NewLine + report.RenderSites());
        }
    }
}
