using FluentAssertions;
using SharedKernel.ArchitectureTests.Observability;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// The suppression audit (WO-087 / P-618 D-03): a suppression of an observability rule in <c>src/</c> fails unless the owning domain's
/// <c>CLAUDE.md</c> names the file and the rule. The real tree carries none.
/// </summary>
public sealed class SuppressionAuditTests
{
    private const string StorageFile = "src/Infrastructure/Storage/SharedKernel.Storage.S3/S3.cs";

    private static readonly HashSet<string> RuleIds = new(["SK0043", "SK0022", "CA2200"], StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, string> NoBrains = new Dictionary<string, string>();

    private static IReadOnlyCollection<SuppressionSource> One(string path, string text) => [new SuppressionSource(path, text)];

    [Theory]
    [InlineData("src/Infrastructure/Storage/SharedKernel.Storage.S3/SharedKernel.Storage.S3.csproj", "<Project><PropertyGroup><NoWarn>$(NoWarn);CS1591;SK0043</NoWarn></PropertyGroup></Project>")]
    [InlineData("src/Infrastructure/Storage/SharedKernel.Storage.S3/SharedKernel.Storage.S3.csproj", "<Project><PropertyGroup><WarningsNotAsErrors>CA2200</WarningsNotAsErrors></PropertyGroup></Project>")]
    [InlineData(StorageFile, "class A\n{\n#pragma warning disable CS0618, CA2200\n    void M() { }\n}")]
    [InlineData(StorageFile, "[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(\"Design\", \"SK0022:Raw literal\", Justification = \"x\")]")]
    [InlineData("src/Infrastructure/Storage/.editorconfig", "[*.cs]\ndotnet_diagnostic.SK0043.severity = none")]
    public void FirePath_AnUnjustifiedSuppression_Throws(string path, string text)
    {
        var act = () => SuppressionAudit.Assert(One(path, text), ObservabilityFixture.Registry(), NoBrains, RuleIds, new StringWriter());

        act.Should().Throw<InvalidOperationException>().WithMessage("*suppression.unjustified*");
    }

    [Fact]
    public void FirePath_TheFindingNamesTheDomainFileAndLine()
    {
        var report = SuppressionAudit.Find(One(StorageFile, "class A\n{\n#pragma warning disable CA2200\n}"), ObservabilityFixture.Registry(), NoBrains, RuleIds);

        var finding = report.Findings.Should().ContainSingle().Subject;
        finding.Domain.Should().Be("08.Storage");
        finding.Site.Should().Be("S3.cs:3");
    }

    [Fact]
    public void PassPath_ASuppressionTheDomainBrainNamesWithFileAndRule_IsJustified()
    {
        var brains = new Dictionary<string, string> { ["08.Storage"] = "## Known Limitations\n- `SharedKernel.Storage.S3/S3.cs` disables CA2200 because the SDK requires it." };

        var report = SuppressionAudit.Find(One(StorageFile, "#pragma warning disable CA2200"), ObservabilityFixture.Registry(), brains, RuleIds);

        report.IsClean.Should().BeTrue();
    }

    [Fact]
    public void FirePath_ABrainThatNamesTheFileButNotTheRule_DoesNotJustify()
    {
        var brains = new Dictionary<string, string> { ["08.Storage"] = "`SharedKernel.Storage.S3/S3.cs` is special." };

        SuppressionAudit.Find(One(StorageFile, "#pragma warning disable CA2200"), ObservabilityFixture.Registry(), brains, RuleIds).IsClean.Should().BeFalse();
    }

    [Fact]
    public void PassPath_TheCodeThatDoesNotSuppressAnObservabilityRule_IsClean()
    {
        var sources = new[]
        {
            new SuppressionSource(StorageFile, "#pragma warning disable CS0618\n[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Design\", \"CA1062\")]\nclass A { }"),
            new SuppressionSource("src/Infrastructure/Storage/X.csproj", "<Project><PropertyGroup><NoWarn>CS1591</NoWarn></PropertyGroup></Project>"),
            new SuppressionSource("src/Infrastructure/Storage/.editorconfig", "dotnet_diagnostic.SK0043.severity = error"),
            new SuppressionSource(StorageFile, "// #pragma warning disable CA2200 is only a comment\nvar text = \"CA2200\";"),
        };

        SuppressionAudit.Find(sources, ObservabilityFixture.Registry(), NoBrains, RuleIds).IsClean.Should().BeTrue();
    }

    [Fact]
    public void RealTree_ProductionCodeCarriesNoUnjustifiedSuppression()
    {
        var graph = RepositoryGraph.Load();
        var registry = PlatformReport.Registry(graph);
        var src = new DirectoryInfo(Path.Combine(graph.Root.FullName, "src"));
        var sources = src.EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => f.Extension is ".cs" or ".csproj" or ".props" or ".targets" or ".editorconfig" or ".globalconfig" || f.Name == ".editorconfig")
            .Select(f => (File: f, Relative: Path.GetRelativePath(graph.Root.FullName, f.FullName).Replace('\\', '/')))
            .Where(f => !f.Relative.Contains("/obj/", StringComparison.Ordinal) && !f.Relative.Contains("/bin/", StringComparison.Ordinal) && !IsTestProject(f.Relative))
            .Select(f => new SuppressionSource(f.Relative, File.ReadAllText(f.File.FullName)))
            .ToList();
        var brains = registry.Domains
            .Select(d => (d.Id, Path: Path.Combine(graph.Root.FullName, d.Folder.Replace('/', Path.DirectorySeparatorChar), "CLAUDE.md")))
            .Where(d => File.Exists(d.Path))
            .ToDictionary(d => d.Id, d => File.ReadAllText(d.Path), StringComparer.Ordinal);
        var ids = KernelSelfAnalysisConfigTests.AnalyzerRuleIds();
        ids.Add("CA2200");

        sources.Should().HaveCountGreaterThan(500, "the audit reads every production source file, not an empty folder");
        SuppressionAudit.Assert(sources, registry, brains, ids, new StringWriter());
    }

    // The self-analysis excludes test projects (decision D-02e), so their suppressions are not audited either.
    private static bool IsTestProject(string relativePath) =>
        relativePath.Split('/').Any(segment => segment.EndsWith(".Tests", StringComparison.Ordinal));
}
