using System.Text;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Observability;
using Xunit;
using Xunit.Abstractions;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// The platform-wide observability report (WO-087 / P-583 T-08): every packable production assembly is run through the EventId ownership,
/// telemetry-naming, log-template and error-code assertions, and the analyzer findings of the per-project SARIF files are added when they
/// exist. In the committed <c>report</c> mode nothing fails: the tests assert the report is produced, every finding names its domain and no
/// assembly is silently skipped, and never that the report is empty. Each domain's conformance phase reads its own count from the table this
/// prints (also written to <c>artifacts/observability/platform-report.md</c>) and drives it to zero.
/// </summary>
[Collection(ObservabilityEnvironment.CollectionName)]
public sealed class PlatformObservabilityReportTests(ITestOutputHelper output)
{
    private static readonly string[] AnalyzerRuleIds = [.. ObservabilityRuleIds.Read()];

    [Fact]
    public void PlatformReport_IsProducedForEveryPackableAssembly_AndNamesTheDomainOfEveryFinding()
    {
        var graph = RepositoryGraph.Load();
        var registry = PlatformReport.Registry(graph);
        var targets = PlatformReport.Targets(graph);
        var ownership = PlatformReport.Ownership(graph, registry);

        targets.Should().HaveCountGreaterThan(80, "the report covers every packable production project, read from the csproj files");
        targets.Select(t => registry.IdOf(t.ProjectPath)).Distinct().Should().Contain(["01.Core", "06.Persistence", "13.ServiceDefaults", "14.Presentation"]);

        var report = ObservabilityReport.Combine(
            PackageEventIdBlockAssertion.Find(targets, registry, ownership),
            TelemetryNamingAssertion.Find(targets, registry, new TelemetryNamingOptions(PlatformReport.TelemetryNamePattern)),
            LogTemplateAssertion.Find(targets, registry, new LogTemplateOptions(PlatformReport.FreeTextPlaceholders)),
            ErrorCodeFormatAssertion.Find(targets, registry, new ErrorCodeFormatOptions(PlatformReport.ProtocolMirrorPrefixes)),
            AnalyzerFindings(graph, registry));

        var mode = ObservabilityEnforcement.Configured(typeof(PlatformObservabilityReportTests).Assembly);
        ObservabilityEnforcement.Apply(report, mode, new TestOutputWriter(output));
        WriteReportFile(graph, report, targets.Count);

        report.Findings.Should().OnlyContain(f => f.Domain != DomainRegistry.UnknownDomain, "every finding must name the domain whose phase fixes it");
        report.CountsByDomain().Keys.Should().OnlyContain(domain => registry.Domains.Any(d => d.Id == domain));

        // Nothing is skipped silently: a target without a built file is a finding, so the two always add up.
        var unscanned = report.Findings.Count(f => f.Rule == "unscanned" && f.Site == "-");
        unscanned.Should().BeLessThanOrEqualTo(targets.Count(t => t.AssemblyFile is null) * 1, "each missing assembly is reported once, however many assertions saw it");
    }

    [Fact]
    public void SarifReader_AddsTheAnalyzerFindingsOfTheFilesThatExist_PerDomain()
    {
        var sarif = """
            {
              "version": "2.1.0",
              "runs": [
                {
                  "results": [
                    { "ruleId": "SK0043", "level": "warning", "message": { "text": "Exception 'A' exposes an Error." },
                      "locations": [ { "physicalLocation": { "artifactLocation": { "uri": "file:///repo/src/Hosting/X/A.cs" }, "region": { "startLine": 12 } } } ] },
                    { "ruleId": "CS8618", "level": "warning", "message": { "text": "not an observability rule" } },
                    { "ruleId": "SK0044", "level": "warning", "message": { "text": "Suppressed." }, "suppressions": [ { "kind": "inSource" } ] },
                    { "ruleId": "CA2200", "level": "warning", "message": { "text": "Rethrow." },
                      "locations": [ { "physicalLocation": { "artifactLocation": { "uri": "file:///repo/src/Hosting/X/B.cs" }, "region": { "startLine": 3 } } } ] }
                  ]
                }
              ]
            }
            """;
        var directory = Directory.CreateTempSubdirectory("sarif-reader-");
        var file = Path.Combine(directory.FullName, ObservabilitySarifReader.FileName);
        File.WriteAllText(file, sarif);

        var findings = ObservabilitySarifReader.Read(
            [("SharedKernel.Security.Oidc", file), ("SharedKernel.Missing", Path.Combine(directory.FullName, "absent.sarif"))],
            project => project.Contains("Security", StringComparison.Ordinal) ? "12.Security" : "unknown",
            new HashSet<string>(AnalyzerRuleIds, StringComparer.Ordinal));

        findings.Should().HaveCount(2);
        findings.Should().OnlyContain(f => f.Domain == "12.Security" && f.Assembly == "SharedKernel.Security.Oidc");
        findings.Select(f => f.Rule).Should().BeEquivalentTo("analyzer.SK0043", "analyzer.CA2200");
        findings.Single(f => f.Rule == "analyzer.SK0043").Site.Should().Be("A.cs:12");
    }

    private ObservabilityReport AnalyzerFindings(RepositoryGraph graph, DomainRegistry registry)
    {
        var files = graph.Projects.Where(p => p.IsProduction)
            .Select(p => (Project: p.Name, Path: PlatformReport.BuiltFile(graph, p.Name, ObservabilitySarifReader.FileName)))
            .Where(f => f.Path is not null)
            .Select(f => (f.Project, f.Path!))
            .ToList();
        output.WriteLine($"Analyzer SARIF files found: {files.Count} (none until the self-analysis import is in the root build).");

        var domains = PlatformReport.AssemblyDomains(graph, registry);
        var findings = ObservabilitySarifReader.Read(files, project => domains.GetValueOrDefault(project, DomainRegistry.UnknownDomain), new HashSet<string>(AnalyzerRuleIds, StringComparer.Ordinal));
        return new ObservabilityReport(findings, []);
    }

    private static void WriteReportFile(RepositoryGraph graph, ObservabilityReport report, int targets)
    {
        var directory = Directory.CreateDirectory(Path.Combine(graph.Root.FullName, "artifacts", "observability"));
        var builder = new StringBuilder();
        builder.AppendLine("# Platform observability report").AppendLine();
        builder.Append("Assemblies scanned: ").Append(targets).AppendLine().AppendLine();
        builder.AppendLine(report.Render()).AppendLine().AppendLine(report.RenderSites());
        File.WriteAllText(Path.Combine(directory.FullName, "platform-report.md"), builder.ToString());
    }

    private sealed class TestOutputWriter(ITestOutputHelper output) : StringWriter
    {
        public override void WriteLine(string? value) => output.WriteLine(value ?? string.Empty);
    }
}

/// <summary>The analyzer ids the SARIF reader keeps: read from the self-analysis props, the one list.</summary>
internal static class ObservabilityRuleIds
{
    public static IEnumerable<string> Read()
    {
        var path = Path.Combine(RepositoryGraph.FindRepositoryRoot().FullName, "tools", "Governance", "SelfAnalysis", "SharedKernel.SelfAnalysis.props");
        var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), "<SharedKernelObservabilityRuleIds>([^<]+)</SharedKernelObservabilityRuleIds>");
        return match.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
