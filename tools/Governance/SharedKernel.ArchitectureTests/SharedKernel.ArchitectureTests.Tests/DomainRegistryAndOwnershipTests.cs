using FluentAssertions;
using SharedKernel.ArchitectureTests.Observability;
using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="DomainRegistry"/> (the root CLAUDE.md table parser) and <see cref="EventIdOwnership"/> (the <c>## Logging</c> table
/// parser), against fixtures and against the real files.
/// </summary>
public sealed class DomainRegistryAndOwnershipTests
{
    [Fact]
    public void Registry_RealRootClaudeMd_ListsAllTwentyOneDomainsAndTheirEventIdBlocks()
    {
        var graph = RepositoryGraph.Load();
        var registry = PlatformReport.Registry(graph);

        registry.Domains.Should().HaveCount(21);
        registry.Domains.Select(d => d.Number).Should().Equal(Enumerable.Range(0, 21));
        registry.Domains.Single(d => d.Id == "06.Persistence").Folder.Should().Be("src/Infrastructure/Persistence");

        // The registry numbers are the LoggingEventIdRanges bases: two registries must not disagree.
        var bases = typeof(LoggingEventIdRanges).GetFields()
            .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(int) && f.Name is not ("DomainRangeWidth" or "PackageSubBlockWidth"))
            .ToDictionary(f => f.Name, f => (int)f.GetRawConstantValue()!);
        foreach (var domain in registry.Domains)
        {
            var name = domain.Id[3..];
            var constant = bases.Keys.FirstOrDefault(k => k == name);
            constant.Should().NotBeNull($"LoggingEventIdRanges has a base for {domain.Id}");
            bases[constant!].Should().Be(domain.EventIdBlockStart, domain.Id);
        }
    }

    [Theory]
    [InlineData("src/Hosting/Security/SharedKernel.Security.Oidc/SharedKernel.Security.Oidc.csproj", "12.Security")]
    [InlineData("src\\Hosting\\ServiceDefaults\\SharedKernel.MultiTenancy\\SharedKernel.MultiTenancy.csproj", "13.ServiceDefaults")]
    [InlineData("src/Foundation/SharedKernel.Cryptography/SharedKernel.Cryptography.csproj", "01.Core")]
    [InlineData("src/Infrastructure/Caching/SharedKernel.Caching.Testing/SharedKernel.Caching.Testing.csproj", "02.Caching")]
    [InlineData("src/Testing/SharedKernel.Testing/SharedKernel.Testing.csproj", "16.Testing")]
    [InlineData("tools/Governance/SharedKernel.Analyzers/SharedKernel.Analyzers.csproj", "00.Governance")]
    [InlineData("samples/Shop/Shop.csproj", DomainRegistry.UnknownDomain)]
    public void Registry_DomainOfAPath_IsTheLongestFolderPrefix(string path, string expected)
    {
        var registry = PlatformReport.Registry(RepositoryGraph.Load());

        registry.IdOf(path).Should().Be(expected);
    }

    [Fact]
    public void Registry_NoDomainRow_Throws()
    {
        var act = () => DomainRegistry.Parse("# nothing here");

        act.Should().Throw<InvalidOperationException>();
    }

    private static readonly IReadOnlyDictionary<string, string> Assemblies = new Dictionary<string, string>
    {
        ["SharedKernel.Caching.FusionCache"] = "02.Caching",
        ["SharedKernel.Caching.Redis.Core"] = "02.Caching",
        ["SharedKernel.Cryptography"] = "01.Core",
        ["SharedKernel.Cryptography.Argon2"] = "01.Core",
        ["SharedKernel.Cryptography.KeyVault.Azure"] = "01.Core",
        ["SharedKernel.Presentation.WebApi"] = "14.Presentation",
        ["SharedKernel.Presentation.Core"] = "14.Presentation",
        ["SharedKernel.Presentation.SignalR"] = "14.Presentation",
    };

    [Fact]
    public void Ownership_RelativeRanges_AreReadFromTheDomainBlock()
    {
        var registry = PlatformReport.Registry(RepositoryGraph.Load());
        var claude = """
            ## Logging

            | Sub-block | Package | Events |
            | --- | --- | --- |
            | +0 – +99 | `Caching.FusionCache` | +0..+7 warmup |
            | +100 – +199 | `Caching.Redis.Core` | +100 connection restored |
            """;

        var ownership = EventIdOwnership.Parse(registry, [("02.Caching", claude)], Assemblies);

        ownership.For("SharedKernel.Caching.FusionCache").Should().ContainSingle().Which.Should().Match<EventIdOwnershipEntry>(e => e.Start == 2000 && e.End == 2099);
        ownership.For("SharedKernel.Caching.Redis.Core").Should().ContainSingle().Which.Should().Match<EventIdOwnershipEntry>(e => e.Start == 2100 && e.End == 2199);
    }

    [Fact]
    public void Ownership_AFamilyRow_SplitsTheSubBlockBaseAndAdapters()
    {
        var registry = PlatformReport.Registry(RepositoryGraph.Load());
        var claude = """
            ## Logging

            | Sub-block | Owner | Status |
            | --- | --- | --- |
            | `1500`–`1599` | `SharedKernel.Cryptography` (+ `.Argon2`, `.KeyVault.Azure` at 50-99) | reserved |
            """;

        var ownership = EventIdOwnership.Parse(registry, [("01.Core", claude)], Assemblies);

        ownership.For("SharedKernel.Cryptography").Should().ContainSingle().Which.Should().Match<EventIdOwnershipEntry>(e => e.Start == 1500 && e.End == 1549);
        var adapters = ownership.For("SharedKernel.Cryptography.Argon2").Should().ContainSingle().Which;
        adapters.Start.Should().Be(1550);
        adapters.End.Should().Be(1599);
        adapters.Assemblies.Should().BeEquivalentTo("SharedKernel.Cryptography.Argon2", "SharedKernel.Cryptography.KeyVault.Azure");
    }

    [Fact]
    public void Ownership_RowsWithNoRangeOrNoMatchingPackage_DeclareNothing()
    {
        var registry = PlatformReport.Registry(RepositoryGraph.Load());
        var claude = """
            ## Logging

            | EventId | Package | Event |
            | --- | --- | --- |
            | 14000, 14001 | `SharedKernel.Presentation.WebApi` | x |

            | Sub-block | Package | In use |
            | --- | --- | --- |
            | — | GraphQL | none yet |
            | 14400–14499 | (unclaimed) | reserved |
            | 14300–14399 | WebApi and Core | shared |
            """;

        var ownership = EventIdOwnership.Parse(registry, [("14.Presentation", claude)], Assemblies);

        ownership.Entries.Should().ContainSingle();
        ownership.Entries[0].Assemblies.Should().BeEquivalentTo("SharedKernel.Presentation.WebApi", "SharedKernel.Presentation.Core");
    }

    [Fact]
    public void Ownership_RealClaudeMdFiles_DeclareTheFamilyAndPackageRangesTheyDocument()
    {
        var graph = RepositoryGraph.Load();
        var registry = PlatformReport.Registry(graph);

        var ownership = PlatformReport.Ownership(graph, registry);

        ownership.For("SharedKernel.Storage.S3").Should().Contain(e => e.Start == 8100 && e.End == 8199);
        ownership.For("SharedKernel.Caching.FusionCache").Should().Contain(e => e.Start == 2000 && e.End == 2099);
        ownership.For("SharedKernel.Cryptography").Should().Contain(e => e.Start == 1500 && e.End == 1549);
        ownership.For("SharedKernel.Cryptography.Argon2").Should().Contain(e => e.Start == 1550 && e.End == 1599);
        ownership.For("SharedKernel.Security.Oidc").Should().Contain(e => e.Start == 12100 && e.End == 12199);
        ownership.For("SharedKernel.Idempotency.EfCore").Should().Contain(e => e.Start == 18100 && e.End == 18199);
    }
}
