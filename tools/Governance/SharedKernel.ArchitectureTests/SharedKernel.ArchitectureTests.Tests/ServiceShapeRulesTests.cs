using FluentAssertions;
using SharedKernel.ArchitectureTests.Helpers;
using SharedKernel.ArchitectureTests.ServiceShape;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// The service-shape rules over in-memory restore graphs. Project names use the <c>Fixture.Svc</c> prefix, which no real
/// project carries (rule 15); kernel package names are real, so their tiers come from the embedded index.
/// </summary>
public sealed class ServiceShapeRulesTests
{
    private const string Svc = "Fixture.Svc";

    private static readonly ServiceShapeOptions Four = new(Svc);
    private static readonly ServiceShapeOptions Six = new(Svc) { Variant = ServiceShapeVariant.SixProject };

    // ----- the graphs ------------------------------------------------------------------------------------------------

    private static Dictionary<string, string[]> FourProjectEdges() =>
        new(StringComparer.Ordinal)
        {
            [$"{Svc}.Domain"] = ["SharedKernel.Domain"],
            [$"{Svc}.Application"] = [$"{Svc}.Domain", "SharedKernel.Application"],
            [$"{Svc}.Infrastructure"] = [$"{Svc}.Application", "SharedKernel.Persistence.EfCore"],
            [$"{Svc}.Api"] = [$"{Svc}.Application", $"{Svc}.Infrastructure", "SharedKernel.Presentation.WebApi"],
            ["SharedKernel.Domain"] = ["SharedKernel.Primitives"],
            ["SharedKernel.Application"] = ["SharedKernel.Domain", "SharedKernel.Primitives"],
            ["SharedKernel.Persistence.EfCore"] = ["SharedKernel.Persistence.Abstractions"],
            ["SharedKernel.Persistence.Abstractions"] = ["SharedKernel.Primitives"],
            ["SharedKernel.Presentation.WebApi"] = ["SharedKernel.Primitives"],
            ["SharedKernel.Primitives"] = [],
        };

    private static Dictionary<string, string[]> SixProjectEdges()
    {
        var edges = FourProjectEdges();
        edges[$"{Svc}.Contracts"] = ["SharedKernel.Contracts"];
        edges[$"{Svc}.Client"] = [$"{Svc}.Contracts", "SharedKernel.Communication.Rest"];
        edges[$"{Svc}.Application"] = [$"{Svc}.Domain", $"{Svc}.Contracts", "SharedKernel.Application"];
        edges["SharedKernel.Contracts"] = [];
        edges["SharedKernel.Communication.Rest"] = ["SharedKernel.Primitives"];
        return edges;
    }

    private static ServiceDependencyGraph Graph(Dictionary<string, string[]> edges) =>
        ServiceDependencyGraph.Create(edges.Keys.Where(name => name.StartsWith(Svc, StringComparison.Ordinal)), edges);

    private static ServiceDependencyGraph GoodFour() => Graph(FourProjectEdges());

    private static ServiceDependencyGraph GoodSix() => Graph(SixProjectEdges());

    private static ServiceDependencyGraph FourWith(Action<Dictionary<string, string[]>> change)
    {
        var edges = FourProjectEdges();
        change(edges);
        return Graph(edges);
    }

    private static ServiceDependencyGraph SixWith(Action<Dictionary<string, string[]>> change)
    {
        var edges = SixProjectEdges();
        change(edges);
        return Graph(edges);
    }

    private static void Append(Dictionary<string, string[]> edges, string project, params string[] references)
    {
        edges[project] = [.. edges[project], .. references];
        foreach (string reference in references.Where(r => !edges.ContainsKey(r)))
        {
            edges[reference] = [];
        }
    }

    // ----- the pass paths --------------------------------------------------------------------------------------------

    [Fact]
    public void FourProject_GoodGraph_EveryRulePasses()
    {
        var graph = GoodFour();
        ServiceShapeRules.FindProjectReferencesFollowTheLayering(graph, Four).Should().BeEmpty();
        ServiceShapeRules.FindDomainSeesFoundationAndModelOnly(graph, Four).Should().BeEmpty();
        ServiceShapeRules.FindApplicationSeesContractsOnly(graph, Four).Should().BeEmpty();
        ServiceShapeRules.FindInfrastructureSeesAdaptersButNoHost(graph, Four).Should().BeEmpty();
        ServiceShapeRules.FindHostIsTheCompositionRoot(graph, Four).Should().BeEmpty();
        ServiceShapeRules.FindNothingReferencesTheHost(graph, Four).Should().BeEmpty();
        ServiceShapeRules.FindProductionNeverReferencesTestingPackages(graph, Four).Should().BeEmpty();
        ServiceShapeRules.FindEveryKernelPackageIsKnown(graph, Four).Should().BeEmpty();
        ServiceShapeRules.FindAll(graph, Four).Should().BeEmpty();
    }

    [Fact]
    public void FourProject_GoodGraph_AssertFormsDoNotThrow()
    {
        var graph = GoodFour();
        ServiceShapeRules.AssertProjectReferencesFollowTheLayering(graph, Four);
        ServiceShapeRules.AssertDomainSeesFoundationAndModelOnly(graph, Four);
        ServiceShapeRules.AssertApplicationSeesContractsOnly(graph, Four);
        ServiceShapeRules.AssertInfrastructureSeesAdaptersButNoHost(graph, Four);
        ServiceShapeRules.AssertHostIsTheCompositionRoot(graph, Four);
        ServiceShapeRules.AssertNothingReferencesTheHost(graph, Four);
        ServiceShapeRules.AssertProductionNeverReferencesTestingPackages(graph, Four);
        ServiceShapeRules.AssertEveryKernelPackageIsKnown(graph, Four);
        ServiceShapeRules.AssertAll(graph, Four);
    }

    [Fact]
    public void SixProject_GoodGraph_EveryRulePasses()
    {
        var graph = GoodSix();
        ServiceShapeRules.FindAll(graph, Six).Should().BeEmpty();
        ServiceShapeRules.FindContractsArePure(graph, Six).Should().BeEmpty();
        ServiceShapeRules.FindClientSeesContractsAndRestOnly(graph, Six).Should().BeEmpty();
        ServiceShapeRules.AssertContractsArePure(graph, Six);
        ServiceShapeRules.AssertClientSeesContractsAndRestOnly(graph, Six);
        ServiceShapeRules.AssertAll(graph, Six);
    }

    [Fact]
    public void SharedProjects_AreIgnoredWhenComparingProjectReferences()
    {
        var graph = FourWith(edges =>
        {
            edges["Fixture.SvcShared.Contracts"] = [];
            Append(edges, $"{Svc}.Application", "Fixture.SvcShared.Contracts");
        });

        ServiceShapeRules.FindProjectReferencesFollowTheLayering(graph, Four).Should().NotBeEmpty();
        ServiceShapeRules
            .FindProjectReferencesFollowTheLayering(graph, new ServiceShapeOptions(Svc) { SharedProjects = ["Fixture.SvcShared.Contracts"] })
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void WorkerHost_IsJudgedLikeTheApi()
    {
        var edges = FourProjectEdges();
        edges[$"{Svc}.Worker"] = edges[$"{Svc}.Api"];
        edges.Remove($"{Svc}.Api");
        var worker = new ServiceShapeOptions(Svc) { HostSuffix = "Worker" };

        ServiceShapeRules.FindAll(Graph(edges), worker).Should().BeEmpty();
        ServiceShapeRules.FindAll(Graph(edges), Four).Should().ContainMatch("*Fixture.Svc.Api is not a project*");
    }

    // ----- the fire paths: four-project ------------------------------------------------------------------------------

    [Fact]
    public void Layering_FiresWhenDomainReferencesAProject()
    {
        var graph = FourWith(edges => Append(edges, $"{Svc}.Domain", $"{Svc}.Application"));

        ServiceShapeRules.FindProjectReferencesFollowTheLayering(graph, Four).Should().ContainMatch("*Fixture.Svc.Domain's project references*");
        FluentActions
            .Invoking(() => ServiceShapeRules.AssertProjectReferencesFollowTheLayering(graph, Four))
            .Should()
            .Throw<ArchitectureRuleViolationException>()
            .WithMessage("*ProjectReferencesFollowTheLayering*");
    }

    [Fact]
    public void Layering_FiresWhenInfrastructureSkipsTheApplication()
    {
        var graph = FourWith(edges => edges[$"{Svc}.Infrastructure"] = [$"{Svc}.Domain", "SharedKernel.Persistence.EfCore"]);

        ServiceShapeRules.FindProjectReferencesFollowTheLayering(graph, Four).Should().ContainMatch("*Fixture.Svc.Infrastructure's project references*");
    }

    [Fact]
    public void Layering_FiresWhenTheHostDoesNotComposeInfrastructure()
    {
        var graph = FourWith(edges => edges[$"{Svc}.Api"] = [$"{Svc}.Application", "SharedKernel.Presentation.WebApi"]);

        ServiceShapeRules.FindProjectReferencesFollowTheLayering(graph, Four).Should().ContainMatch("*Fixture.Svc.Api's project references*");
    }

    [Fact]
    public void Layering_FiresWhenAServiceProjectIsMissingFromTheGraph()
    {
        var edges = FourProjectEdges();
        edges.Remove($"{Svc}.Infrastructure");

        ServiceShapeRules
            .FindProjectReferencesFollowTheLayering(Graph(edges), Four)
            .Should()
            .ContainMatch("*Fixture.Svc.Infrastructure is not a project*");
        ServiceShapeRules.FindProjectReferencesFollowTheLayering(Graph(FourProjectEdges()), new ServiceShapeOptions("Fixture.Typo")).Should().NotBeEmpty();
    }

    [Fact]
    public void Domain_FiresWhenItReachesAnAbstractionsPackage()
    {
        var graph = FourWith(edges => Append(edges, $"{Svc}.Domain", "SharedKernel.Persistence.Abstractions"));

        ServiceShapeRules.FindDomainSeesFoundationAndModelOnly(graph, Four).Should().ContainMatch("*Abstractions*");
        FluentActions
            .Invoking(() => ServiceShapeRules.AssertDomainSeesFoundationAndModelOnly(graph, Four))
            .Should()
            .Throw<ArchitectureRuleViolationException>();
    }

    [Fact]
    public void Application_FiresWhenItReachesAnAdapter()
    {
        var graph = FourWith(edges => Append(edges, $"{Svc}.Application", "SharedKernel.Persistence.EfCore"));

        ServiceShapeRules.FindApplicationSeesContractsOnly(graph, Four).Should().ContainMatch("*Adapter*");
    }

    [Fact]
    public void Application_FiresWhenItReachesMediatR()
    {
        var graph = FourWith(edges => Append(edges, $"{Svc}.Application", "MediatR"));

        ServiceShapeRules.FindApplicationSeesContractsOnly(graph, Four).Should().ContainMatch("*MediatR*");
        FluentActions
            .Invoking(() => ServiceShapeRules.AssertApplicationSeesContractsOnly(graph, Four))
            .Should()
            .Throw<ArchitectureRuleViolationException>();
    }

    [Fact]
    public void Infrastructure_FiresWhenItReferencesAHostPackage()
    {
        var graph = FourWith(edges => Append(edges, $"{Svc}.Infrastructure", "SharedKernel.Presentation.WebApi"));

        ServiceShapeRules.FindInfrastructureSeesAdaptersButNoHost(graph, Four).Should().ContainMatch("*Host*");
        FluentActions
            .Invoking(() => ServiceShapeRules.AssertInfrastructureSeesAdaptersButNoHost(graph, Four))
            .Should()
            .Throw<ArchitectureRuleViolationException>();
    }

    [Fact]
    public void Infrastructure_FiresWhenItReferencesAnAbstractionsPackageDirectly()
    {
        var graph = FourWith(edges => Append(edges, $"{Svc}.Infrastructure", "SharedKernel.Caching.Abstractions"));

        ServiceShapeRules.FindInfrastructureSeesAdaptersButNoHost(graph, Four).Should().ContainMatch("*direct kernel packages*");
    }

    [Fact]
    public void Host_FiresWhenItReferencesAnAdapterDirectly()
    {
        var graph = FourWith(edges => Append(edges, $"{Svc}.Api", "SharedKernel.Persistence.EfCore"));

        ServiceShapeRules.FindHostIsTheCompositionRoot(graph, Four).Should().ContainMatch("*Adapter*");
        FluentActions
            .Invoking(() => ServiceShapeRules.AssertHostIsTheCompositionRoot(graph, Four))
            .Should()
            .Throw<ArchitectureRuleViolationException>();
    }

    [Fact]
    public void NothingReferencesTheHost_FiresWhenInfrastructureReachesIt()
    {
        var graph = FourWith(edges => Append(edges, $"{Svc}.Infrastructure", $"{Svc}.Api"));

        ServiceShapeRules.FindNothingReferencesTheHost(graph, Four).Should().ContainMatch("*Fixture.Svc.Infrastructure must never reach Fixture.Svc.Api*");
        FluentActions
            .Invoking(() => ServiceShapeRules.AssertNothingReferencesTheHost(graph, Four))
            .Should()
            .Throw<ArchitectureRuleViolationException>();
    }

    [Fact]
    public void Production_FiresWhenItReachesATestingPackage()
    {
        var graph = FourWith(edges => Append(edges, $"{Svc}.Infrastructure", "SharedKernel.Testing"));

        ServiceShapeRules
            .FindProductionNeverReferencesTestingPackages(graph, Four)
            .Should()
            .ContainMatch("*Fixture.Svc.Infrastructure must never reach a Testing-tier package*");
        FluentActions
            .Invoking(() => ServiceShapeRules.AssertProductionNeverReferencesTestingPackages(graph, Four))
            .Should()
            .Throw<ArchitectureRuleViolationException>();
    }

    [Fact]
    public void EveryKernelPackageIsKnown_FiresForAnUnlistedKernelPackage()
    {
        var graph = FourWith(edges => Append(edges, $"{Svc}.Infrastructure", "SharedKernel.Fixture.NotAPackage"));

        ServiceShapeRules.FindEveryKernelPackageIsKnown(graph, Four).Should().ContainMatch("*SharedKernel.Fixture.NotAPackage*");
        FluentActions
            .Invoking(() => ServiceShapeRules.AssertEveryKernelPackageIsKnown(graph, Four))
            .Should()
            .Throw<ArchitectureRuleViolationException>();
    }

    [Fact]
    public void FindAll_PrefixesEachFindingWithItsRule()
    {
        var graph = FourWith(edges => Append(edges, $"{Svc}.Domain", "SharedKernel.Persistence.Abstractions"));

        ServiceShapeRules.FindAll(graph, Four).Should().ContainMatch("DomainSeesFoundationAndModelOnly: *");
        FluentActions
            .Invoking(() => ServiceShapeRules.AssertAll(graph, Four))
            .Should()
            .Throw<ArchitectureRuleViolationException>();
    }

    // ----- the fire paths: six-project -------------------------------------------------------------------------------

    [Fact]
    public void Contracts_FiresWhenItReferencesAProject()
    {
        var graph = SixWith(edges => Append(edges, $"{Svc}.Contracts", $"{Svc}.Domain"));

        var findings = ServiceShapeRules.FindContractsArePure(graph, Six);
        findings.Should().ContainMatch("*Fixture.Svc.Contracts's project references*");
        findings.Should().ContainMatch("*Fixture.Svc.Contracts must never reach Fixture.Svc.Domain*");
        FluentActions
            .Invoking(() => ServiceShapeRules.AssertContractsArePure(graph, Six))
            .Should()
            .Throw<ArchitectureRuleViolationException>();
    }

    [Fact]
    public void Contracts_FiresWhenTheDomainReachesIt()
    {
        var graph = SixWith(edges => Append(edges, $"{Svc}.Domain", $"{Svc}.Contracts"));

        ServiceShapeRules.FindContractsArePure(graph, Six).Should().ContainMatch("*Fixture.Svc.Domain must never reach Fixture.Svc.Contracts*");
    }

    [Fact]
    public void Contracts_FiresWhenItTakesAnotherKernelPackage()
    {
        var graph = SixWith(edges => Append(edges, $"{Svc}.Contracts", "SharedKernel.Domain"));

        ServiceShapeRules.FindContractsArePure(graph, Six).Should().ContainMatch("*Fixture.Svc.Contracts's kernel packages*");
    }

    [Fact]
    public void Client_FiresWhenItReferencesTheDomain()
    {
        var graph = SixWith(edges => Append(edges, $"{Svc}.Client", $"{Svc}.Domain"));

        ServiceShapeRules.FindClientSeesContractsAndRestOnly(graph, Six).Should().ContainMatch("*Fixture.Svc.Client's project references*");
        FluentActions
            .Invoking(() => ServiceShapeRules.AssertClientSeesContractsAndRestOnly(graph, Six))
            .Should()
            .Throw<ArchitectureRuleViolationException>();
    }

    [Fact]
    public void Client_FiresWhenItTakesAnotherKernelPackage()
    {
        var graph = SixWith(edges => Append(edges, $"{Svc}.Client", "SharedKernel.Persistence.EfCore"));

        ServiceShapeRules.FindClientSeesContractsAndRestOnly(graph, Six).Should().ContainMatch("*Fixture.Svc.Client's kernel packages*");
    }

    [Fact]
    public void SixProject_NothingReferencesTheHost_CoversTheClient()
    {
        var graph = SixWith(edges => Append(edges, $"{Svc}.Client", $"{Svc}.Api"));

        ServiceShapeRules.FindNothingReferencesTheHost(graph, Six).Should().ContainMatch("*Fixture.Svc.Client must never reach Fixture.Svc.Api*");
    }

    [Fact]
    public void SixProject_ApplicationMustReferenceContracts()
    {
        var graph = SixWith(edges => edges[$"{Svc}.Application"] = [$"{Svc}.Domain", "SharedKernel.Application"]);

        ServiceShapeRules.FindProjectReferencesFollowTheLayering(graph, Six).Should().ContainMatch("*Fixture.Svc.Application's project references*");
    }

    [Fact]
    public void SixProjectRules_RejectTheFourProjectVariant()
    {
        FluentActions
            .Invoking(() => ServiceShapeRules.FindContractsArePure(GoodFour(), Four))
            .Should()
            .Throw<InvalidOperationException>();
        FluentActions
            .Invoking(() => ServiceShapeRules.FindClientSeesContractsAndRestOnly(GoodFour(), Four))
            .Should()
            .Throw<InvalidOperationException>();
    }

    // ----- the graph reader ------------------------------------------------------------------------------------------

    [Fact]
    public void FromDepsJson_ReadsProjectsPackagesAndEdges()
    {
        const string json = """
            {
              "targets": { ".NETCoreApp,Version=v10.0": {
                "Fixture.Svc.Domain/1.0.0": { "dependencies": { "SharedKernel.Domain": "1.0.0" } },
                "SharedKernel.Domain/1.0.0": { "dependencies": { "SharedKernel.Primitives": "1.0.0" } },
                "SharedKernel.Primitives/1.0.0": {}
              } },
              "libraries": {
                "Fixture.Svc.Domain/1.0.0": { "type": "project" },
                "SharedKernel.Domain/1.0.0": { "type": "package" },
                "SharedKernel.Primitives/1.0.0": { "type": "package" }
              }
            }
            """;

        var graph = ServiceDependencyGraph.FromDepsJson(json);

        graph.ContainsProject("Fixture.Svc.Domain").Should().BeTrue();
        graph.ContainsProject("SharedKernel.Domain").Should().BeFalse();
        graph.DirectKernelPackages("Fixture.Svc.Domain").Should().Equal("SharedKernel.Domain");
        graph.DirectProjects("Fixture.Svc.Domain").Should().BeEmpty();
        graph.Closure("Fixture.Svc.Domain").Should().BeEquivalentTo("SharedKernel.Domain", "SharedKernel.Primitives");
    }

    [Fact]
    public void Load_ReadsTheDepsJsonOfARealTestAssembly()
    {
        var graph = ServiceDependencyGraph.Load("SharedKernel.ArchitectureTests.Tests");

        graph.ContainsProject("SharedKernel.ArchitectureTests").Should().BeTrue();
        graph.ContainsProject("SharedKernel.Domain").Should().BeTrue();
        graph.DirectProjects("SharedKernel.ArchitectureTests").Should().BeEmpty("the package references nothing in the kernel");
        graph.Closure("SharedKernel.ArchitectureTests.Tests").Should().Contain("SharedKernel.Domain");
    }
}
