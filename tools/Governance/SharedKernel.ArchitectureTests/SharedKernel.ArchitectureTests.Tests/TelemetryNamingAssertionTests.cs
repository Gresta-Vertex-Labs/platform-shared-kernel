using FluentAssertions;
using SharedKernel.ArchitectureTests.Observability;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="TelemetryNamingAssertion"/> (WO-087 / P-583 D-08): a non-prefixed name, a non-constant name, a name with no
/// matching public const, a unit omitted, a unit given, a unit from a field (<c>unverifiable</c>) and a conforming fixture.
/// </summary>
public sealed class TelemetryNamingAssertionTests
{
    private static readonly TelemetryNamingOptions Options = new(PlatformReport.TelemetryNamePattern);

    private static ObservabilityReport Find(string name, string source)
    {
        var registry = ObservabilityFixture.Registry();
        var target = ObservabilityFixture.Target(name, ObservabilityFixture.StorageFolder, source);
        return TelemetryNamingAssertion.Find([target], registry, Options);
    }

    private const string Usings = "using System; using System.Diagnostics; using System.Diagnostics.Metrics;\n";

    [Fact]
    public void Find_ConformingFixture_NoFindingAndNothingUnverifiable()
    {
        var report = Find("Fixture.Telemetry.Ok", Usings + """
            namespace Fixture.Telemetry.Ok
            {
                public static class StorageTelemetryNames
                {
                    public const string SourceName = "SharedKernel.Storage";
                }

                public sealed class Instruments
                {
                    private static readonly ActivitySource Source = new(StorageTelemetryNames.SourceName);

                    public Instruments(IMeterFactory factory)
                    {
                        var meter = new Meter(StorageTelemetryNames.SourceName);
                        Duration = meter.CreateHistogram<double>("sharedkernel.storage.operation.duration", "s", "Duration.");
                        Bytes = meter.CreateCounter<long>("sharedkernel.storage.bytes", unit: "By");
                        Gauge = meter.CreateObservableGauge("sharedkernel.storage.open", () => 1, "{handle}");
                        FromFactory = factory.Create(StorageTelemetryNames.SourceName);
                    }

                    public Histogram<double> Duration { get; }
                    public Counter<long> Bytes { get; }
                    public ObservableGauge<int> Gauge { get; }
                    public Meter FromFactory { get; }
                    public Activity? Start() => Source.StartActivity("storage get");
                }
            }
            """);

        report.IsClean.Should().BeTrue(report.RenderSites());
        report.Unverifiable.Should().BeEmpty(report.RenderSites());
    }

    [Fact]
    public void Find_NameOutsideThePattern_Reports()
    {
        var report = Find("Fixture.Telemetry.Prefix", Usings + """
            namespace Fixture.Telemetry.Prefix
            {
                public static class Names { public const string Source = "consumer.telemetry"; public const string Lower = "SharedKernel.lower"; }

                public sealed class T
                {
                    private static readonly ActivitySource A = new(Names.Source);
                    private static readonly Meter M = new(Names.Lower);
                }
            }
            """);

        report.Findings.Where(f => f.Rule == "telemetry.name-not-prefixed").Select(f => f.Detail)
            .Should().HaveCount(2)
            .And.Contain(d => d.Contains("consumer.telemetry", StringComparison.Ordinal))
            .And.Contain(d => d.Contains("SharedKernel.lower", StringComparison.Ordinal));
        report.Findings.Should().NotContain(f => f.Rule == "telemetry.name-not-constant");
    }

    [Fact]
    public void Find_NameThatIsNotAPublicConst_Reports()
    {
        var report = Find("Fixture.Telemetry.NotConst", Usings + """
            namespace Fixture.Telemetry.NotConst
            {
                public static class Names
                {
                    public static readonly string Source = "SharedKernel.Fields";
                    internal const string Hidden = "SharedKernel.Hidden";
                }

                public sealed class T
                {
                    private static readonly ActivitySource A = new(Names.Source);
                    private static readonly Meter Inline = new("SharedKernel.Inline");
                    private static readonly Meter Internal = new(Names.Hidden);
                }
            }
            """);

        report.Findings.Where(f => f.Rule == "telemetry.name-not-constant").Select(f => f.Detail)
            .Should().HaveCount(3)
            .And.Contain(d => d.Contains("not a compile-time constant", StringComparison.Ordinal))
            .And.Contain(d => d.Contains("SharedKernel.Inline", StringComparison.Ordinal))
            .And.Contain(d => d.Contains("SharedKernel.Hidden", StringComparison.Ordinal));
        report.Findings.Should().NotContain(f => f.Rule == "telemetry.name-not-prefixed");
    }

    [Fact]
    public void Find_UnitOmittedOrEmpty_ReportsAndUnitGivenPasses()
    {
        var report = Find("Fixture.Telemetry.Unit", Usings + """
            namespace Fixture.Telemetry.Unit
            {
                public static class Names { public const string Source = "SharedKernel.Units"; }

                public sealed class T
                {
                    public T()
                    {
                        var meter = new Meter(Names.Source);
                        meter.CreateCounter<long>("omitted");
                        meter.CreateHistogram<double>("empty", "");
                        meter.CreateUpDownCounter<long>("given", "{item}");
                        meter.CreateObservableCounter("observable-omitted", () => 1L);
                    }
                }
            }
            """);

        report.Findings.Where(f => f.Rule == "telemetry.unit-missing").Select(f => f.Detail)
            .Should().HaveCount(3)
            .And.Contain(d => d.StartsWith("CreateCounter", StringComparison.Ordinal))
            .And.Contain(d => d.StartsWith("CreateHistogram", StringComparison.Ordinal) && d.Contains("empty", StringComparison.Ordinal))
            .And.Contain(d => d.StartsWith("CreateObservableCounter", StringComparison.Ordinal));
        report.Unverifiable.Should().BeEmpty();
    }

    [Fact]
    public void Find_UnitFromAFieldOrParameter_IsUnverifiableNotAViolation()
    {
        var report = Find("Fixture.Telemetry.Unverifiable", Usings + """
            namespace Fixture.Telemetry.Unverifiable
            {
                public static class Names { public const string Source = "SharedKernel.Unverifiable"; }

                public sealed class T
                {
                    private static readonly string Seconds = "s";

                    public T(Meter meter, string unit)
                    {
                        meter.CreateHistogram<double>("from-field", Seconds);
                        meter.CreateCounter<long>("from-parameter", unit);
                        _ = new Meter(Names.Source);
                    }
                }
            }
            """);

        report.IsClean.Should().BeTrue(report.RenderSites());
        report.Unverifiable.Should().HaveCount(2).And.OnlyContain(f => f.Rule == "telemetry.unit-unverifiable");
    }

    [Fact]
    public void Find_ExemptNameWithAReason_Passes()
    {
        var options = new TelemetryNamingOptions(PlatformReport.TelemetryNamePattern, new Dictionary<string, string> { ["Npgsql"] = "Npgsql's own source, named in 06.Persistence CLAUDE.md" });
        var registry = ObservabilityFixture.Registry();
        var target = ObservabilityFixture.Target("Fixture.Telemetry.Exempt", ObservabilityFixture.StorageFolder, Usings + """
            namespace Fixture.Telemetry.Exempt
            {
                public static class Names { public const string Source = "Npgsql"; }
                public sealed class T { private static readonly ActivitySource A = new(Names.Source); }
            }
            """);

        TelemetryNamingAssertion.Find([target], registry, options).IsClean.Should().BeTrue();
        TelemetryNamingAssertion.Find([target], registry, Options).Findings.Should().ContainSingle(f => f.Rule == "telemetry.name-not-prefixed");
    }

    [Fact]
    public void Options_AnExemptionWithoutAReasonOrAnInvalidPattern_IsRejected()
    {
        var noReason = () => new TelemetryNamingOptions(PlatformReport.TelemetryNamePattern, new Dictionary<string, string> { ["X"] = " " });
        var badPattern = () => new TelemetryNamingOptions("[");

        noReason.Should().Throw<ArgumentException>();
        badPattern.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Assert_EnforceThrowsOnAViolation_ReportDoesNotAndUnverifiableNeverFails()
    {
        var registry = ObservabilityFixture.Registry();
        var violating = ObservabilityFixture.Target("Fixture.Telemetry.Enforce", ObservabilityFixture.StorageFolder, Usings + """
            namespace Fixture.Telemetry.Enforce
            {
                public static class Names { public const string Source = "SharedKernel.Enforce"; }
                public sealed class T { public T() { new Meter(Names.Source).CreateCounter<long>("no-unit"); } }
            }
            """);
        var unverifiableOnly = ObservabilityFixture.Target("Fixture.Telemetry.EnforceOk", ObservabilityFixture.StorageFolder, Usings + """
            namespace Fixture.Telemetry.EnforceOk
            {
                public static class Names { public const string Source = "SharedKernel.EnforceOk"; }
                public sealed class T { public T(Meter meter, string unit) { _ = new Meter(Names.Source); meter.CreateCounter<long>("x", unit); } }
            }
            """);

        TelemetryNamingAssertion.Find([violating], registry, Options).Findings.Should().NotBeEmpty();
        var enforce = () => TelemetryNamingAssertion.Assert([violating], registry, Options, new StringWriter());
        enforce.Should().Throw<InvalidOperationException>().WithMessage("*telemetry.unit-missing*");

        var report = TelemetryNamingAssertion.Assert([unverifiableOnly], registry, Options, new StringWriter());
        report.Unverifiable.Should().NotBeEmpty();
    }

    [Fact]
    public void RealAssembly_MassTransitCore_NamesItsActivitySourceByAPrefixedPublicConstant()
    {
        var graph = RepositoryGraph.Load();
        var registry = PlatformReport.Registry(graph);
        var target = PlatformReport.TargetFor(graph, "SharedKernel.Messaging.MassTransit");

        var report = TelemetryNamingAssertion.Find([target], registry, Options);

        report.Findings.Where(f => f.Rule != "unscanned").Should().BeEmpty(report.RenderSites());

        // Not vacuous: the same assembly does create a source, so a pattern it cannot match is reported.
        var strict = TelemetryNamingAssertion.Find([target], registry, new TelemetryNamingOptions(@"^Nothing\.Matches$"));
        strict.Findings.Should().Contain(f => f.Rule == "telemetry.name-not-prefixed" && f.Detail.Contains("SharedKernel.Messaging", StringComparison.Ordinal));
    }
}
