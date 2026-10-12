using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis.Diagnostics;
using SharedKernel.ArchitectureTests.Observability;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// The report/enforce switch (WO-087 / P-583 D-01, D-06): the committed default is <c>report</c>, <see cref="ObservabilityEnforcement.Apply"/> never
/// throws in report mode and throws one aggregate in enforce mode, the environment override wins, and the analyzer severity files and the
/// <c>WarningsNotAsErrors</c> list follow the same property and cover every id the analyzer assembly reports.
/// </summary>
[Collection(ObservabilityEnvironment.CollectionName)]
public sealed partial class ObservabilitySwitchTests
{
    private static readonly DirectoryInfo SelfAnalysis = new(Path.Combine(RepositoryGraph.FindRepositoryRoot().FullName, "tools", "Governance", "SelfAnalysis"));

    private static ObservabilityFinding Finding(string domain, string rule, string assembly = "A") => new(rule, domain, assembly, "T::M", "detail");

    private static string ModeInProps() =>
        XDocument.Load(Path.Combine(SelfAnalysis.FullName, "ObservabilityMode.props"))
            .Descendants().Single(e => e.Name.LocalName == "SharedKernelObservabilityMode").Value.Trim();

    [Fact]
    public void CommittedDefault_IsReport()
    {
        ModeInProps().Should().Be("report", "the observability rules ship in report mode; P-618 flips this one value");
    }

    [Fact]
    public void Configured_ReadsTheStampedMode_AndTheEnvironmentVariableWins()
    {
        using (ObservabilityEnvironment.Override(null))
        {
            ObservabilityEnforcement.Configured(typeof(ObservabilitySwitchTests).Assembly).Should().Be(ObservabilityMode.Report);
        }

        using (ObservabilityEnvironment.Override("enforce"))
        {
            ObservabilityEnforcement.Configured(typeof(ObservabilitySwitchTests).Assembly).Should().Be(ObservabilityMode.Enforce);
        }

        using (ObservabilityEnvironment.Override("not-a-mode"))
        {
            var act = () => ObservabilityEnforcement.Configured(typeof(ObservabilitySwitchTests).Assembly);
            act.Should().Throw<InvalidOperationException>().WithMessage("*not-a-mode*");
        }
    }

    [Fact]
    public void Configured_AnAssemblyWithNoStamp_DefaultsToReport()
    {
        using var _ = ObservabilityEnvironment.Override(null);

        ObservabilityEnforcement.Configured(typeof(object).Assembly).Should().Be(ObservabilityMode.Report);
    }

    [Fact]
    public void BothConsumers_ReadTheSameProperty()
    {
        var testsProject = File.ReadAllText(Path.Combine(RepositoryGraph.FindRepositoryRoot().FullName, "tools", "Governance", "SharedKernel.ArchitectureTests", "SharedKernel.ArchitectureTests.Tests", "SharedKernel.ArchitectureTests.Tests.csproj"));
        var selfAnalysisProps = File.ReadAllText(Path.Combine(SelfAnalysis.FullName, "SharedKernel.SelfAnalysis.props"));

        testsProject.Should().Contain(@"SelfAnalysis\ObservabilityMode.props").And.Contain("$(SharedKernelObservabilityMode)");
        selfAnalysisProps.Should().Contain("ObservabilityMode.props").And.Contain("kernel-$(SharedKernelObservabilityMode).globalconfig");

        using var _ = ObservabilityEnvironment.Override(null);
        var stamped = typeof(ObservabilitySwitchTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == ObservabilityEnforcement.ModeMetadataKey).Value;
        stamped.Should().Be(ModeInProps(), "the assembly is stamped from the property the props file defines (unless a -p: override was passed to this build)");
    }

    [Fact]
    public void Apply_ReportMode_NeverThrowsAndPrintsOneRowPerDomainAndRule()
    {
        var report = new ObservabilityReport(
            [Finding("06.Persistence", "eventid.block-span"), Finding("06.Persistence", "eventid.block-span", "B"), Finding("06.Persistence", "telemetry.unit-missing"), Finding("08.Storage", "errorcode.format")],
            [Finding("08.Storage", "telemetry.unit-unverifiable")]);
        var writer = new StringWriter();

        ObservabilityEnforcement.Apply(report, ObservabilityMode.Report, writer);

        var text = writer.ToString();
        text.Should().Contain("| 06.Persistence | eventid.block-span | 2 |")
            .And.Contain("| 06.Persistence | telemetry.unit-missing | 1 |")
            .And.Contain("| 08.Storage | errorcode.format | 1 |")
            .And.Contain("Total findings: 4; unverifiable: 1.");
        report.CountsByDomain().Keys.Should().Equal("06.Persistence", "08.Storage");
    }

    [Fact]
    public void Apply_EnforceMode_ThrowsOneAggregateNamingEverySite_AndUnverifiableAloneNeverFails()
    {
        var failing = new ObservabilityReport([Finding("06.Persistence", "eventid.block-span"), Finding("08.Storage", "errorcode.format")], []);
        var unverifiableOnly = new ObservabilityReport([], [Finding("08.Storage", "telemetry.unit-unverifiable")]);

        var act = () => ObservabilityEnforcement.Apply(failing, ObservabilityMode.Enforce, new StringWriter());
        act.Should().Throw<InvalidOperationException>().WithMessage("*2 violation(s)*").WithMessage("*eventid.block-span*").WithMessage("*errorcode.format*");

        var calm = () => ObservabilityEnforcement.Apply(unverifiableOnly, ObservabilityMode.Enforce, new StringWriter());
        calm.Should().NotThrow();
        var empty = () => ObservabilityEnforcement.Apply(ObservabilityReport.Empty, ObservabilityMode.Enforce, new StringWriter());
        empty.Should().NotThrow();
    }

    [Fact]
    public void Combine_DeduplicatesTheSameUnscannedFindingSeenByEveryAssertion()
    {
        var unscanned = Finding("08.Storage", "unscanned");

        var combined = ObservabilityReport.Combine(new ObservabilityReport([unscanned], []), new ObservabilityReport([unscanned], []), new ObservabilityReport([Finding("08.Storage", "x")], []));

        combined.Findings.Should().HaveCount(2);
    }

    [Fact]
    public void WarningsNotAsErrorsList_AndBothSeverityFiles_CoverEveryDescriptorOfTheAnalyzerAssembly()
    {
        var reported = typeof(SharedKernel.Analyzers.Diagnostics.AnalyzerBase).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .SelectMany(t => ((DiagnosticAnalyzer)Activator.CreateInstance(t)!).SupportedDiagnostics.Select(d => d.Id))
            .ToHashSet(StringComparer.Ordinal);
        reported.Should().Contain(["SK0001", "SK0043", "SK0044"]);

        var props = File.ReadAllText(Path.Combine(SelfAnalysis.FullName, "SharedKernel.SelfAnalysis.props"));
        var listed = RuleIdList().Match(props).Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        listed.Should().BeEquivalentTo([.. reported, "CA2200"], "a new analyzer id must be added to SharedKernelObservabilityRuleIds");

        foreach (var (mode, severity) in new[] { ("report", "warning"), ("enforce", "error") })
        {
            var severities = SeverityFile(mode);
            severities.Keys.Should().BeEquivalentTo([.. reported, "CA2200"], $"kernel-{mode}.globalconfig fixes the severity of every id");
            severities["SK0034"].Should().Be("suggestion", "SK0034 is advisory by decision, in both modes");
            severities["SK0044"].Should().Be(severity, "SK0044 ships disabled and the kernel enables it");
            severities.Where(p => p.Key != "SK0034").Should().OnlyContain(p => p.Value == severity);
        }
    }

    [Fact]
    public void SelfAnalysisProps_GuardsItsScopeAndReferencesTheAnalyzerWithoutAPackageDependency()
    {
        var props = File.ReadAllText(Path.Combine(SelfAnalysis.FullName, "SharedKernel.SelfAnalysis.props"));

        props.Should().Contain("OutputItemType=\"Analyzer\"").And.Contain("ReferenceOutputAssembly=\"false\"").And.Contain("PrivateAssets=\"all\"");
        props.Should().Contain("'$(SharedKernelTier)' != 'Tooling'").And.Contain("'$(IsTestProject)' != 'true'").And.Contain("'$(OutputType)' != 'Exe'");
        props.Should().Contain("WarningsNotAsErrors").And.Contain("sharedkernel-observability.sarif");

        // A static GlobalAnalyzerConfigFiles item is copied into EditorConfigFiles during evaluation, which a Directory.Build.targets
        // import comes too late for: the severity file would silently never reach the compiler. It is added inside a target instead.
        props.Should().Contain("<EditorConfigFiles Include=").And.NotContain("<GlobalAnalyzerConfigFiles Include=");
    }

    private static Dictionary<string, string> SeverityFile(string mode) =>
        File.ReadAllLines(Path.Combine(SelfAnalysis.FullName, $"kernel-{mode}.globalconfig"))
            .Select(line => SeverityLine().Match(line))
            .Where(m => m.Success)
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);

    [GeneratedRegex(@"<SharedKernelObservabilityRuleIds>([^<]+)</SharedKernelObservabilityRuleIds>")]
    private static partial Regex RuleIdList();

    [GeneratedRegex(@"^dotnet_diagnostic\.(\w+)\.severity\s*=\s*(\w+)\s*$")]
    private static partial Regex SeverityLine();
}

/// <summary>Serialises the tests that read or set the process-wide override variable, and restores it afterwards.</summary>
[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class ObservabilityEnvironment
{
    public const string CollectionName = "ObservabilityEnvironment";

    /// <summary>Sets (or clears, for <see langword="null"/>) the override for the duration of the returned scope.</summary>
    public static IDisposable Override(string? value)
    {
        var previous = Environment.GetEnvironmentVariable(ObservabilityEnforcement.ModeEnvironmentVariable);
        Environment.SetEnvironmentVariable(ObservabilityEnforcement.ModeEnvironmentVariable, value);
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(ObservabilityEnforcement.ModeEnvironmentVariable, previous);
    }
}
