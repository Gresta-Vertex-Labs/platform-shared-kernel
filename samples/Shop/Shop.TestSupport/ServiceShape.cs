using FluentAssertions;

namespace Shop.TestSupport;

/// <summary>
/// The rules of a consuming service's projects, checked against the real restore graph: what the Domain, Application,
/// Infrastructure and Api (or Worker) projects of one service may reference.
/// </summary>
/// <param name="graph">The restore graph of the service's test assembly.</param>
/// <param name="service">The service prefix, e.g. <c>Shop.Catalog</c>.</param>
/// <param name="host">The composition-root project suffix: <c>Api</c> or <c>Worker</c>.</param>
public sealed class ServiceShape(DependencyGraph graph, string service, string host = "Api")
{
    private readonly KernelPackageIndex _index = KernelPackageIndex.Instance;

    public string Domain => $"{service}.Domain";

    public string Application => $"{service}.Application";

    public string Infrastructure => $"{service}.Infrastructure";

    public string Host => $"{service}.{host}";

    /// <summary>Domain ← Application ← Infrastructure; the host composes the Application and the Infrastructure.</summary>
    public void ProjectReferencesFollowTheLayering(params string[] sharedProjects)
    {
        graph
            .DirectProjects(Domain)
            .Except(sharedProjects)
            .Should()
            .BeEmpty("the domain depends on no other project");
        graph.DirectProjects(Application).Except(sharedProjects).Should().BeEquivalentTo([Domain]);
        graph
            .DirectProjects(Infrastructure)
            .Except(sharedProjects)
            .Should()
            .BeEquivalentTo([Application]);
        graph
            .DirectProjects(Host)
            .Except(sharedProjects)
            .Should()
            .BeEquivalentTo(
                [Application, Infrastructure],
                "the host sends to the Application and registers the Infrastructure"
            );
    }

    /// <summary>The domain sees the kernel's Foundation and Model packages only.</summary>
    public void DomainSeesFoundationAndModelOnly() =>
        KernelTiersOf(graph.Closure(Domain)).Should().BeSubsetOf(["Foundation", "Model"]);

    /// <summary>Handlers are written against contracts; the pipeline, the mediator and the adapters belong to the host.</summary>
    public void ApplicationSeesContractsOnly()
    {
        KernelTiersOf(graph.Closure(Application))
            .Should()
            .BeSubsetOf(["Foundation", "Model", "Abstractions"]);
        graph
            .Closure(Application)
            .Should()
            .NotContain(p => p.StartsWith("MediatR", StringComparison.Ordinal));
    }

    /// <summary>The Infrastructure adds adapters (and Foundation helpers), never a host package.</summary>
    public void InfrastructureSeesAdaptersButNoHost()
    {
        KernelTiersOf(graph.Closure(Infrastructure))
            .Should()
            .BeSubsetOf(["Foundation", "Model", "Abstractions", "Adapter"]);
        graph
            .DirectKernelPackages(Infrastructure)
            .Select(_index.TierOf)
            .Should()
            .OnlyContain(tier => tier == "Adapter" || tier == "Foundation");
    }

    /// <summary>The host references host packages directly; everything else arrives through its projects.</summary>
    public void HostIsTheCompositionRoot() =>
        graph.DirectKernelPackages(Host).Select(_index.TierOf).Should().AllBe("Host");

    /// <summary>No production project of the service reaches a Testing-tier package.</summary>
    public void ProductionNeverReferencesTestingPackages()
    {
        foreach (string project in new[] { Domain, Application, Infrastructure, Host })
        {
            graph
                .Closure(project)
                .Where(p => _index.TierOf(p) == "Testing")
                .Should()
                .BeEmpty(project);
        }
    }

    /// <summary>Every SharedKernel package in the graph is in the index: none is unknown or renamed.</summary>
    public void EveryKernelPackageIsKnown() =>
        new[] { Domain, Application, Infrastructure, Host }
            .SelectMany(graph.Closure)
            .Where(p => p.StartsWith("SharedKernel.", StringComparison.Ordinal))
            .Where(p => _index.TierOf(p) == "ThirdParty")
            .Should()
            .BeEmpty("docs/packages.md lists every kernel package");

    private IEnumerable<string> KernelTiersOf(IEnumerable<string> packages) =>
        packages
            .Where(p => p.StartsWith("SharedKernel.", StringComparison.Ordinal))
            .Select(_index.TierOf)
            .Distinct();
}
