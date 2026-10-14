using System.Text;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Observability;
using Xunit;
using Xunit.Abstractions;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// The platform-wide observability assertions (WO-087 / P-618): every packable production assembly is run through the EventId ownership,
/// telemetry-naming, log-template and error-code assertions. A finding fails the test with one aggregated message grouped by domain, and a
/// missing assembly (<c>unscanned</c>) is a finding too, so nothing is skipped silently. The per-domain table is also written to
/// <c>artifacts/observability/platform-report.md</c>. The analyzer rules are build errors and are not repeated here.
/// </summary>
public sealed class PlatformObservabilityReportTests(ITestOutputHelper output)
{
    [Fact]
    public void Platform_HasNoObservabilityFinding_AndNoUnscannedAssembly()
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
            ErrorCodeFormatAssertion.Find(targets, registry, new ErrorCodeFormatOptions(PlatformReport.ProtocolMirrorPrefixes)));

        WriteReportFile(graph, report, targets.Count);
        report.Findings.Should().OnlyContain(f => f.Domain != DomainRegistry.UnknownDomain, "every finding must name the domain whose code fixes it");

        report.Findings.Where(f => f.Rule == "unscanned").Should().BeEmpty("every packable assembly must be built before the architecture tests run");
        ObservabilityEnforcement.Apply(report, new TestOutputWriter(output));
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
