using System.Reflection;

namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>
/// The one switch between "report" and "enforce" for the platform-wide observability assertions.
/// </summary>
/// <remarks>
/// The mode is the MSBuild property <c>SharedKernelObservabilityMode</c> in <c>tools/Governance/SelfAnalysis/ObservabilityMode.props</c>;
/// the architecture test project stamps it into its assembly as <c>AssemblyMetadata("SharedKernelObservabilityMode", …)</c> and
/// <see cref="Configured"/> reads it back, so the analyzers' severity and these assertions follow one value. The environment variable
/// <c>SHAREDKERNEL_OBSERVABILITY_MODE</c> overrides it for a local experiment. Every assertion also takes the mode as a parameter, so a
/// firing test proves the enforce path while the committed default stays <see cref="ObservabilityMode.Report"/>.
/// </remarks>
public static class ObservabilityEnforcement
{
    /// <summary>The assembly metadata key the mode is stamped under.</summary>
    public const string ModeMetadataKey = "SharedKernelObservabilityMode";

    /// <summary>The environment variable that overrides the stamped mode.</summary>
    public const string ModeEnvironmentVariable = "SHAREDKERNEL_OBSERVABILITY_MODE";

    /// <summary>Reads the configured mode: the environment override if set, else the assembly metadata, else <see cref="ObservabilityMode.Report"/>.</summary>
    /// <param name="carrier">The assembly the build stamped the mode into (the calling test assembly).</param>
    /// <returns>The mode.</returns>
    /// <exception cref="InvalidOperationException">A value is present but is neither <c>report</c> nor <c>enforce</c>.</exception>
    public static ObservabilityMode Configured(Assembly carrier)
    {
        ArgumentNullException.ThrowIfNull(carrier);

        var overridden = Environment.GetEnvironmentVariable(ModeEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
            return Parse(overridden, ModeEnvironmentVariable);

        var stamped = carrier.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == ModeMetadataKey)?.Value;
        return string.IsNullOrWhiteSpace(stamped) ? ObservabilityMode.Report : Parse(stamped, ModeMetadataKey);
    }

    /// <summary>
    /// Writes the per-domain table, then fails in <see cref="ObservabilityMode.Enforce"/> when the report has a finding.
    /// <see cref="ObservabilityReport.Unverifiable"/> entries never fail.
    /// </summary>
    /// <param name="report">The report to apply.</param>
    /// <param name="mode">Whether findings fail.</param>
    /// <param name="output">Where the table goes; <see cref="Console.Out"/> when <see langword="null"/>.</param>
    /// <exception cref="InvalidOperationException">Thrown once, aggregating every finding, in enforce mode.</exception>
    public static void Apply(ObservabilityReport report, ObservabilityMode mode, TextWriter? output = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        var writer = output ?? Console.Out;
        writer.WriteLine($"Observability report ({(mode == ObservabilityMode.Enforce ? "enforce" : "report")} mode)");
        writer.WriteLine(report.Render());

        if (mode == ObservabilityMode.Enforce && !report.IsClean)
        {
            throw new InvalidOperationException(
                $"Observability assertions found {report.Findings.Count} violation(s):" + Environment.NewLine + report.Render() + Environment.NewLine + report.RenderSites());
        }
    }

    private static ObservabilityMode Parse(string value, string source) =>
        value.Trim().ToLowerInvariant() switch
        {
            "report" => ObservabilityMode.Report,
            "enforce" => ObservabilityMode.Enforce,
            _ => throw new InvalidOperationException($"{source} is '{value}'; it must be 'report' or 'enforce'."),
        };
}
