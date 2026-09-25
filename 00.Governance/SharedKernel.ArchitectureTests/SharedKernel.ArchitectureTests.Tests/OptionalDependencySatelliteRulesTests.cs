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
        grpc!.ProjectReferences.Should().Contain("SharedKernel.Presentation.Core", "the shared attributes and status maps live there");
        graph.Find("SharedKernel.Presentation.WebApi")!.ProjectReferences.Should().Contain("SharedKernel.Presentation.Core");
    }

    [Fact]
    public void PresentationCore_IsHostTier_WithNoAspNetCoreOrPresentationDependency()
    {
        var graph = Graph.Value;
        var core = graph.Find("SharedKernel.Presentation.Core");
        core.Should().NotBeNull();

        core!.Tier.Should().Be("Host");
        core.ProjectReferences.Should().Equal(["SharedKernel.Primitives"]);
        File.ReadAllText(Path.Combine(RepositoryRoot(), core.RelativePath))
            .Should().NotContain("Microsoft.AspNetCore.App", "the shared boundary vocabulary needs no ASP.NET Core");
    }

    [Fact]
    public void PresentationSignalR_ReferencesNoRedis_TheBackplaneIsItsOwnPackage()
    {
        var graph = Graph.Value;
        var signalR = graph.Find("SharedKernel.Presentation.SignalR");
        var redis = graph.Find("SharedKernel.Presentation.SignalR.Redis");
        signalR.Should().NotBeNull();
        redis.Should().NotBeNull();

        ProductionClosure(graph, signalR!).SelectMany(p => p.RuntimePackages)
            .Should().NotContain(p => p.Contains("Redis", StringComparison.OrdinalIgnoreCase));
        redis!.Tier.Should().Be("Host");
        redis.RuntimePackages.Should().Contain("Microsoft.AspNetCore.SignalR.StackExchangeRedis");
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
        var references = typeof(SharedKernel.Presentation.Grpc.Results.GrpcResultExtensions).Assembly
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

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Platform.SharedKernel.slnx not found above the test output directory.");
    }
}
