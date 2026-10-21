using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.ArchitectureTests.ServiceShape;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>The embedded kernel package-tier index: it parses, equals the generated package index, and fails loudly when malformed.</summary>
public sealed partial class KernelPackageIndexTests
{
    [Fact]
    public void Instance_ParsesTheEmbeddedResource()
    {
        var index = KernelPackageIndex.Instance;

        index.Tiers.Should().NotBeEmpty();
        index.Tiers.Values.Should().OnlyContain(tier => tier != KernelPackageIndex.ThirdParty);
    }

    [Fact]
    public void Instance_EqualsThePackageRowsOfTheGeneratedPackageIndex()
    {
        // docs/packages.md is generated from each packable project's tier and checked by eng/generate-package-index.cs
        // --check, so equality here proves the embedded table is current until the generator emits it itself.
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        string? tier = null;
        foreach (string line in File.ReadLines(Path.Combine(RepositoryGraph.FindRepositoryRoot().FullName, "docs", "packages.md")))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                tier = line[3..].Trim();
            }
            else if (tier is not null && PackageRow().Match(line) is { Success: true } row)
            {
                expected[row.Groups["id"].Value] = tier;
            }
        }

        expected.Should().NotBeEmpty();
        KernelPackageIndex.Instance.Tiers.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Instance_CoversEveryPackableProductionProject()
    {
        var packable = RepositoryGraph.Load().Projects.Where(p => p.IsProduction && p.Tier is not null).Select(p => p.Name);

        KernelPackageIndex.Instance.Tiers.Keys.Should().BeEquivalentTo(packable);
    }

    [Fact]
    public void Instance_IsOrdinalSortedWithACommentLine()
    {
        string resource = ReadEmbedded();

        resource.Should().StartWith("# ").And.NotContain("\r").And.EndWith("\n");
        var ids = resource.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(line => line.Split('\t')[0]).ToList();
        ids.Should().Equal(ids.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("SharedKernel.Domain", "Model")]
    [InlineData("SharedKernel.Primitives", "Foundation")]
    [InlineData("SharedKernel.Application", "Abstractions")]
    [InlineData("SharedKernel.Persistence.EfCore", "Adapter")]
    [InlineData("SharedKernel.Presentation.WebApi", "Host")]
    [InlineData("SharedKernel.Testing", "Testing")]
    [InlineData("SharedKernel.ArchitectureTests", "Tooling")]
    public void TierOf_ReturnsTheDeclaredTier(string package, string tier) =>
        KernelPackageIndex.Instance.TierOf(package).Should().Be(tier);

    [Theory]
    [InlineData("Microsoft.Extensions.Logging")]
    [InlineData("MediatR")]
    [InlineData("SharedKernel.Fixture.NotAPackage")]
    public void TierOf_AnUnknownIdIsThirdParty(string package) =>
        KernelPackageIndex.Instance.TierOf(package).Should().Be(KernelPackageIndex.ThirdParty);

    [Theory]
    [InlineData("SharedKernel.Domain", true)]
    [InlineData("SharedKernel", false)]
    [InlineData("MediatR", false)]
    public void IsKernelPackage_MatchesThePrefix(string package, bool expected) =>
        KernelPackageIndex.IsKernelPackage(package).Should().Be(expected);

    [Fact]
    public void Parse_RejectsAnEmptyTable() =>
        FluentActions
            .Invoking(() => KernelPackageIndex.Parse(["# only a comment", ""]))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*no packages*");

    [Theory]
    [InlineData("SharedKernel.A")]
    [InlineData("SharedKernel.A\tNotATier")]
    [InlineData("SharedKernel.A\tModel\textra")]
    [InlineData("\tModel")]
    public void Parse_RejectsAMalformedLine(string line) =>
        FluentActions
            .Invoking(() => KernelPackageIndex.Parse(["# comment", line]))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*malformed*");

    [Fact]
    public void Parse_RejectsADuplicateId() =>
        FluentActions
            .Invoking(() => KernelPackageIndex.Parse(["SharedKernel.A\tModel", "SharedKernel.A\tModel"]))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*twice*");

    [Fact]
    public void Parse_AcceptsACommentThenRows()
    {
        var index = KernelPackageIndex.Parse(["# comment", "SharedKernel.A\tModel", "SharedKernel.B\tHost"]);

        index.TierOf("SharedKernel.A").Should().Be("Model");
        index.TierOf("SharedKernel.B").Should().Be("Host");
    }

    private static string ReadEmbedded()
    {
        using var stream = typeof(KernelPackageIndex).Assembly.GetManifestResourceStream("kernel-package-tiers.tsv")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"^\| \[`(?<id>SharedKernel\.[A-Za-z0-9.]+)`\]")]
    private static partial Regex PackageRow();
}
