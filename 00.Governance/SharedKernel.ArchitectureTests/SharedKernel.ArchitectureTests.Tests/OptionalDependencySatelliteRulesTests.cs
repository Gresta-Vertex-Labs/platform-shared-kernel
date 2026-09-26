using FluentAssertions;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Optional dependencies live in satellite packages (WO-086 / P-570): a service references a broker client, EF Core,
/// Redis or the HTTP API stack only when it uses them. These tests lock the split at the project-file level, over the
/// same repository graph as the tier tests, including transitive project references.
/// </summary>
public sealed partial class DependencyGraphRulesTests
{
    private const string MassTransitCore = "SharedKernel.Messaging.MassTransit";

    [Fact]
    public void MassTransitCore_ReferencesNoTransportAzureOrEfCorePackage()
    {
        var graph = Graph.Value;
        var core = graph.Find(MassTransitCore);
        core.Should().NotBeNull();

        var forbidden = new[] { "MassTransit.RabbitMQ", "MassTransit.Azure.ServiceBus.Core", "Azure.Identity", "MassTransit.EntityFrameworkCore" };
        var closure = ProductionClosure(graph, core!);

        closure.SelectMany(p => p.RuntimePackages.Select(package => $"{p.Name} -> {package}"))
            .Where(edge => forbidden.Any(f => edge.EndsWith($"-> {f}", StringComparison.Ordinal))
                || edge.Contains("-> Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .Should().BeEmpty("RabbitMQ, Azure Service Bus and the EF Core outbox are the .RabbitMq, .AzureServiceBus and .EfCore satellites");

        closure.Select(p => p.Name)
            .Should().NotContain(n => n.StartsWith(MassTransitCore + ".", StringComparison.Ordinal),
                "the core never references its own satellites");
    }

    [Theory]
    [InlineData("SharedKernel.Messaging.MassTransit.RabbitMq", "MassTransit.RabbitMQ")]
    [InlineData("SharedKernel.Messaging.MassTransit.AzureServiceBus", "MassTransit.Azure.ServiceBus.Core")]
    [InlineData("SharedKernel.Messaging.MassTransit.EfCore", "MassTransit.EntityFrameworkCore")]
    public void MassTransitSatellites_AreAdapters_WithADeclaredEdgeToTheCore(string satellite, string package)
    {
        var node = Graph.Value.Find(satellite);
        node.Should().NotBeNull();

        node!.Tier.Should().Be("Adapter");
        node.ProjectReferences.Should().Equal([MassTransitCore], "a satellite plugs into the core and nothing else");
        node.AllowedAdapters.Should().Contain(MassTransitCore, "an adapter-to-adapter edge is declared in the csproj");
        node.RuntimePackages.Should().Contain(package);
    }

    [Fact]
    public void PresentationGrpc_DoesNotReachWebApi()
    {
        var graph = Graph.Value;
        var grpc = graph.Find("SharedKernel.Presentation.Grpc");
        grpc.Should().NotBeNull();

        var closure = ProductionClosure(graph, grpc!).Select(p => p.Name).ToList();

        closure.Should().NotContain("SharedKernel.Presentation.WebApi", "a gRPC host must not pull the HTTP API stack");
        grpc!.ProjectReferences.Should().Contain("SharedKernel.Presentation.Core",
            "the endpoint authorization attributes and policies and the error presentation live there");
        graph.Find("SharedKernel.Presentation.WebApi")!.ProjectReferences.Should().Contain("SharedKernel.Presentation.Core");
    }

    /// <summary>
    /// P-579: <c>SharedKernel.Presentation.Core</c> holds what the HTTP, SignalR and gRPC boundaries share (the endpoint
    /// authorization attributes and policies, error presentation, the error-type status map). The authorization policies
    /// need ASP.NET Core, which the Host tier allows; it must never depend on a sibling presentation package, or a gRPC
    /// host would pull the HTTP API stack through it.
    /// </summary>
    [Fact]
    public void PresentationCore_IsHostTier_WithNoPresentationDependency()
    {
        var graph = Graph.Value;
        var core = graph.Find("SharedKernel.Presentation.Core");
        core.Should().NotBeNull();

        core!.Tier.Should().Be("Host");
        ProductionClosure(graph, core).Select(p => p.Name)
            .Where(name => name != core.Name && name.StartsWith("SharedKernel.Presentation.", StringComparison.Ordinal))
            .Should().BeEmpty("the shared presentation core references no sibling presentation package");
    }

    [Fact]
    public void GraphQL_IsAPresentationHostPackage()
    {
        var graph = Graph.Value;

        graph.Find("SharedKernel.Communication.GraphQL").Should().BeNull("it moved to 14.Presentation");
        var graphQl = graph.Find("SharedKernel.Presentation.GraphQL");
        graphQl.Should().NotBeNull();
        graphQl!.Tier.Should().Be("Host");
        graphQl.RelativePath.Replace('\\', '/').Should().StartWith("14.Presentation/");
    }

    [Fact]
    public void MassTransitCoreAssembly_LoadsNoBrokerClientOrEfCore()
    {
        var references = typeof(SharedKernel.Messaging.MassTransit.Extensions.MessagingBusBuilder).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        references.Should().Contain("MassTransit");
        references.Should().NotContain(n =>
            n.StartsWith("MassTransit.RabbitMq", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("MassTransit.Azure", StringComparison.Ordinal)
            || n.StartsWith("MassTransit.EntityFramework", StringComparison.Ordinal)
            || n.StartsWith("Azure.", StringComparison.Ordinal)
            || n.StartsWith("RabbitMQ.", StringComparison.Ordinal)
            || n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void PresentationGrpcAssembly_DoesNotLoadWebApi()
    {
        var references = typeof(SharedKernel.Presentation.Grpc.GrpcHostBuilderExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        references.Should().NotContain("SharedKernel.Presentation.WebApi");
        references.Should().Contain("SharedKernel.Presentation.Core");
    }

    private static List<ProjectNode> ProductionClosure(RepositoryGraph graph, ProjectNode start)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ProjectNode>();
        var pending = new Stack<ProjectNode>([start]);

        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node.Name))
                continue;

            result.Add(node);
            foreach (var reference in node.ProjectReferences.Select(graph.Find).OfType<ProjectNode>())
                pending.Push(reference);
        }

        return result;
    }
}
