using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.CodeAnalysis.Diagnostics;
using SharedKernel.ArchitectureTests.Observability;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// The enforced end state of the kernel self-analysis (WO-087 / P-618): <c>kernel.globalconfig</c> fixes the severity of every id the
/// analyzer assembly reports, no report plumbing remains, and <see cref="ObservabilityEnforcement.Apply"/> has one behaviour.
/// </summary>
public sealed partial class KernelSelfAnalysisConfigTests
{
    private static readonly DirectoryInfo SelfAnalysis = new(Path.Combine(RepositoryGraph.FindRepositoryRoot().FullName, "tools", "Governance", "SelfAnalysis"));

    internal static HashSet<string> AnalyzerRuleIds() =>
        typeof(SharedKernel.Analyzers.Diagnostics.AnalyzerBase).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .SelectMany(t => ((DiagnosticAnalyzer)Activator.CreateInstance(t)!).SupportedDiagnostics.Select(d => d.Id))
            .ToHashSet(StringComparer.Ordinal);

    private static ObservabilityFinding Finding(string domain, string rule, string assembly = "A") => new(rule, domain, assembly, "T::M", "detail");

    [Fact]
    public void KernelGlobalConfig_FixesEveryDescriptorAsAnError_SuggestionForSk0034_AndEnablesSk0044()
    {
        var reported = AnalyzerRuleIds();
        reported.Should().Contain(["SK0001", "SK0043", "SK0044"]);

        var severities = File.ReadAllLines(Path.Combine(SelfAnalysis.FullName, "kernel.globalconfig"))
            .Select(line => SeverityLine().Match(line))
            .Where(m => m.Success)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);

        severities.Keys.Should().BeEquivalentTo([.. reported, "CA2200"], "a new analyzer id must be added to kernel.globalconfig");
        severities["SK0034"].Should().Be("suggestion", "SK0034 is advisory by decision");
        severities["SK0044"].Should().Be("error", "SK0044 ships disabled and the kernel enables it");
        severities["CA2200"].Should().Be("error");
        severities.Where(p => p.Key != "SK0034").Should().OnlyContain(p => p.Value == "error");
    }

    [Fact]
    public void NoReportPlumbing_RemainsInTheSelfAnalysisFolder()
    {
        SelfAnalysis.GetFiles().Select(f => f.Name).Should().BeEquivalentTo(["SharedKernel.SelfAnalysis.props", "kernel.globalconfig", "verify-self-analysis.sh"]);

        var props = File.ReadAllText(Path.Combine(SelfAnalysis.FullName, "SharedKernel.SelfAnalysis.props"));
        props.Should().NotContain("WarningsNotAsErrors<").And.NotContain("ErrorLog").And.NotContain("SharedKernelObservabilityMode").And.NotContain("sarif");
    }

    [Fact]
    public void SelfAnalysisProps_GuardsItsScope_ImportsOneGlobalConfig_AndReferencesTheAnalyzerWithoutAPackageDependency()
    {
        var props = File.ReadAllText(Path.Combine(SelfAnalysis.FullName, "SharedKernel.SelfAnalysis.props"));

        props.Should().Contain("OutputItemType=\"Analyzer\"").And.Contain("ReferenceOutputAssembly=\"false\"").And.Contain("PrivateAssets=\"all\"");
        props.Should().Contain("'$(SharedKernelTier)' != 'Tooling'").And.Contain("'$(IsTestProject)' != 'true'").And.Contain("'$(OutputType)' != 'Exe'");
        props.Should().Contain("kernel.globalconfig");

        // A static GlobalAnalyzerConfigFiles item is copied into EditorConfigFiles during evaluation, which a Directory.Build.targets
        // import comes too late for: the severity file would silently never reach the compiler. It is added inside a target instead.
        props.Should().Contain("<EditorConfigFiles Include=").And.NotContain("<GlobalAnalyzerConfigFiles Include=");
    }

    [Fact]
    public void Apply_ThrowsOneAggregateNamingEverySite_AndUnverifiableAloneNeverFails()
    {
        var failing = new ObservabilityReport([Finding("06.Persistence", "eventid.block-span"), Finding("08.Storage", "errorcode.format")], []);
        var unverifiableOnly = new ObservabilityReport([], [Finding("08.Storage", "telemetry.unit-unverifiable")]);
        var writer = new StringWriter();

        var act = () => ObservabilityEnforcement.Apply(failing, writer);
        act.Should().Throw<InvalidOperationException>().WithMessage("*2 violation(s)*").WithMessage("*eventid.block-span*").WithMessage("*errorcode.format*");
        writer.ToString().Should().Contain("| 06.Persistence | eventid.block-span | 1 |");

        var calm = () => ObservabilityEnforcement.Apply(unverifiableOnly, new StringWriter());
        calm.Should().NotThrow();
        var empty = () => ObservabilityEnforcement.Apply(ObservabilityReport.Empty, new StringWriter());
        empty.Should().NotThrow();
    }

    [Fact]
    public void Combine_DeduplicatesTheSameUnscannedFindingSeenByEveryAssertion()
    {
        var unscanned = Finding("08.Storage", "unscanned");

        var combined = ObservabilityReport.Combine(new ObservabilityReport([unscanned], []), new ObservabilityReport([unscanned], []), new ObservabilityReport([Finding("08.Storage", "x")], []));

        combined.Findings.Should().HaveCount(2);
    }

    [GeneratedRegex(@"^dotnet_diagnostic\.(\w+)\.severity\s*=\s*(\w+)\s*$")]
    private static partial Regex SeverityLine();
}
