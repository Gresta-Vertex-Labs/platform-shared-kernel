using FluentAssertions;
using SharedKernel.ArchitectureTests.Observability;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="PackageEventIdBlockAssertion"/>, <see cref="EventIdOwnership"/> and <see cref="DomainRegistry"/>
/// (WO-087 / P-583 D-07): fixture assemblies for an id spanning two sub-blocks, two assemblies sharing a block, an id outside the domain
/// block and a conforming pair; enforce mode throws, report mode does not.
/// </summary>
public sealed class PackageEventIdBlockAssertionTests
{
    private static string LogSource(string ns, params int[] ids)
    {
        var methods = string.Concat(ids.Select(id =>
            $"[LoggerMessage(EventId = {id}, Level = LogLevel.Information, Message = \"Event {id}.\")] public static void E{id}(ILogger logger) {{ }}\n"));
        return $"using Microsoft.Extensions.Logging;\nnamespace {ns} {{ public static class Log {{ {methods} }} }}";
    }

    private static EventIdOwnership NoDeclarations(DomainRegistry registry) =>
        EventIdOwnership.Parse(registry, [], new Dictionary<string, string>());

    [Fact]
    public void Find_ConformingPair_NoFinding()
    {
        var registry = ObservabilityFixture.Registry();
        var targets = new[]
        {
            ObservabilityFixture.Target("Fixture.EventIds.OkA", ObservabilityFixture.StorageFolder, LogSource("Fixture.EventIds.OkA", 8100, 8101, 8105)),
            ObservabilityFixture.Target("Fixture.EventIds.OkB", ObservabilityFixture.StorageFolder, LogSource("Fixture.EventIds.OkB", 8200, 8203)),
        };

        var report = PackageEventIdBlockAssertion.Find(targets, registry, NoDeclarations(registry));

        report.IsClean.Should().BeTrue(report.RenderSites());
    }

    [Fact]
    public void Find_IdsSpanningTwoSubBlocks_ReportsTheOutlier()
    {
        var registry = ObservabilityFixture.Registry();
        var target = ObservabilityFixture.Target("Fixture.EventIds.Span", ObservabilityFixture.StorageFolder, LogSource("Fixture.EventIds.Span", 8100, 8101, 8102, 8250));

        var report = PackageEventIdBlockAssertion.Find([target], registry, NoDeclarations(registry));

        report.Findings.Should().ContainSingle(f => f.Rule == "eventid.block-span" && f.Site.EndsWith("::E8250", StringComparison.Ordinal));
        report.Findings.Should().OnlyContain(f => f.Domain == "08.Storage");
    }

    [Fact]
    public void Find_TwoAssembliesInOneSubBlock_ReportsBothSides()
    {
        var registry = ObservabilityFixture.Registry();
        var targets = new[]
        {
            ObservabilityFixture.Target("Fixture.EventIds.ShareA", ObservabilityFixture.CachingFolder, LogSource("Fixture.EventIds.ShareA", 2001, 2002)),
            ObservabilityFixture.Target("Fixture.EventIds.ShareB", ObservabilityFixture.CachingFolder, LogSource("Fixture.EventIds.ShareB", 2010)),
        };

        var report = PackageEventIdBlockAssertion.Find(targets, registry, NoDeclarations(registry));

        report.Findings.Where(f => f.Rule == "eventid.shared-block").Select(f => f.Assembly)
            .Should().BeEquivalentTo("Fixture.EventIds.ShareA", "Fixture.EventIds.ShareB");
    }

    [Fact]
    public void Find_IdOutsideTheDomainBlock_Reports()
    {
        var registry = ObservabilityFixture.Registry();
        var target = ObservabilityFixture.Target("Fixture.EventIds.Outside", ObservabilityFixture.StorageFolder, LogSource("Fixture.EventIds.Outside", 9100));

        var report = PackageEventIdBlockAssertion.Find([target], registry, NoDeclarations(registry));

        report.Findings.Should().ContainSingle(f => f.Rule == "eventid.outside-domain-block")
            .Which.Detail.Should().Contain("9100").And.Contain("8000-8999");
    }

    [Fact]
    public void Find_MissingAssemblyFile_IsUnscannedNotSkipped()
    {
        var registry = ObservabilityFixture.Registry();
        var target = new ScanTarget("Fixture.EventIds.Missing", "src/Infrastructure/Storage/X/X.csproj", Path.Combine(Path.GetTempPath(), "does-not-exist.dll"));

        var report = PackageEventIdBlockAssertion.Find([target], registry, NoDeclarations(registry));

        report.Findings.Should().ContainSingle(f => f.Rule == "unscanned" && f.Domain == "08.Storage");
    }

    [Fact]
    public void Find_DeclaredRanges_AreHonouredAndEnforced()
    {
        var registry = ObservabilityFixture.Registry();
        var claude = """
            ## Logging

            | Sub-block | Package | Events |
            | --- | --- | --- |
            | +0 – +99 | `Fixture.EventIds.Core` | x |
            | +100 – +199 | `Fixture.EventIds.Redis` | x |
            """;
        var ownership = EventIdOwnership.Parse(
            registry,
            [("02.Caching", claude)],
            new Dictionary<string, string>
            {
                ["Fixture.EventIds.Core"] = "02.Caching",
                ["Fixture.EventIds.Redis"] = "02.Caching",
            });

        var targets = new[]
        {
            ObservabilityFixture.Target("Fixture.EventIds.Core", ObservabilityFixture.CachingFolder, LogSource("Fixture.EventIds.Core", 2010, 2011)),
            // Declared 2100-2199: 2250 lies outside it.
            ObservabilityFixture.Target("Fixture.EventIds.Redis", ObservabilityFixture.CachingFolder, LogSource("Fixture.EventIds.Redis", 2100, 2250)),
        };

        var report = PackageEventIdBlockAssertion.Find(targets, registry, ownership);

        report.Findings.Should().ContainSingle(f => f.Rule == "eventid.outside-declared-range")
            .Which.Site.Should().EndWith("::E2250");
        report.Findings.Should().NotContain(f => f.Assembly == "Fixture.EventIds.Core");
    }

    [Fact]
    public void Find_DeclaredRangesThatOverlapOrLeaveTheDomain_Report()
    {
        var registry = ObservabilityFixture.Registry();
        var claude = """
            ## Logging

            | Sub-block | Package |
            | --- | --- |
            | 2000–2099 | `Fixture.A` |
            | 2050–2150 | `Fixture.B` |
            | 3000–3099 | `Fixture.C` |
            """;
        var ownership = EventIdOwnership.Parse(
            registry,
            [("02.Caching", claude)],
            new Dictionary<string, string> { ["Fixture.A"] = "02.Caching", ["Fixture.B"] = "02.Caching", ["Fixture.C"] = "02.Caching" });

        var report = PackageEventIdBlockAssertion.Find([], registry, ownership);

        report.Findings.Select(f => f.Rule).Should().BeEquivalentTo("eventid.declared-overlap", "eventid.declared-outside-domain");
    }

    [Fact]
    public void Assert_EnforceThrowsOneAggregate_ReportDoesNot()
    {
        var registry = ObservabilityFixture.Registry();
        var target = ObservabilityFixture.Target("Fixture.EventIds.Enforce", ObservabilityFixture.StorageFolder, LogSource("Fixture.EventIds.Enforce", 8100, 8300));
        var writer = new StringWriter();

        var report = PackageEventIdBlockAssertion.Assert([target], registry, NoDeclarations(registry), ObservabilityMode.Report, writer);
        report.Findings.Should().NotBeEmpty();
        writer.ToString().Should().Contain("08.Storage").And.Contain("eventid.block-span");

        var enforce = () => PackageEventIdBlockAssertion.Assert([target], registry, NoDeclarations(registry), ObservabilityMode.Enforce, new StringWriter());
        enforce.Should().Throw<InvalidOperationException>().WithMessage("*eventid.block-span*");
    }

    [Fact]
    public void RealAssembly_ConformingPlatformPackage_PassesAgainstTheRealRegistryAndTables()
    {
        // SharedKernel.Storage.S3 declares 8100-8199 in its CLAUDE.md and logs 8100-8105 only.
        var graph = RepositoryGraph.Load();
        var registry = PlatformReport.Registry(graph);
        var ownership = PlatformReport.Ownership(graph, registry);
        var target = PlatformReport.TargetFor(graph, "SharedKernel.Storage.S3");

        var report = PackageEventIdBlockAssertion.Find([target], registry, ownership);

        report.Findings.Where(f => f.Rule != "unscanned").Should().BeEmpty(report.RenderSites());
    }
}
