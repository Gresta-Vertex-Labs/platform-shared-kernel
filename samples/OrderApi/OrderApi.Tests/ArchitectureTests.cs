using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace OrderApi.Tests;

/// <summary>
/// The four-project shape of a service built on the kernel, asserted against the real restore graph.
/// </summary>
/// <remarks>
/// <para>
/// The graph comes from this test assembly's own <c>deps.json</c>, which lists every project and package the build
/// resolved together with each one's direct dependencies — including references the compiler would drop because
/// no type from them is used. Walking it gives each project's transitive closure, so a rule such as "Application
/// never sees MediatR" also catches MediatR arriving through some other package.
/// </para>
/// <para>
/// The rules mirror the kernel's own tier matrix (<c>eng/SharedKernelTiers.targets</c>) applied to a consumer:
/// Domain sees Foundation and Model packages; Application adds Abstractions; Infrastructure adds Adapters; only
/// the Api sees Host packages. A kernel package missing from <see cref="KernelTiers"/> fails the test, so a new
/// reference is classified before it is accepted.
/// </para>
/// </remarks>
public sealed class ArchitectureTests
{
    private const string Domain = "OrderApi.Domain";
    private const string Application = "OrderApi.Application";
    private const string Infrastructure = "OrderApi.Infrastructure";
    private const string Api = "OrderApi.Api";

    private static readonly DependencyGraph Graph = DependencyGraph.Load();

    [Fact]
    public void ProjectReferences_FollowTheLayering()
    {
        Graph.DirectProjects(Domain).Should().BeEmpty("the domain depends on no other project of the service");
        Graph.DirectProjects(Application).Should().BeEquivalentTo([Domain]);
        Graph.DirectProjects(Infrastructure).Should().BeEquivalentTo([Application]);
        Graph.DirectProjects(Api).Should().BeEquivalentTo([Application, Infrastructure],
            "the Api composes the service: it sends to the Application and registers the Infrastructure");
    }

    [Fact]
    public void Domain_ReferencesOnlySharedKernelDomain()
    {
        Graph.DirectKernelPackages(Domain).Should().BeEquivalentTo(["SharedKernel.Domain"]);
        TiersOf(Graph.Closure(Domain)).Should().BeSubsetOf(["Foundation", "Model"],
            "a domain model sees only the kernel's foundation and model packages");
    }

    [Fact]
    public void Application_SeesContractsOnly()
    {
        Graph.DirectKernelPackages(Application).Should().BeEquivalentTo(["SharedKernel.Application"]);
        TiersOf(Graph.Closure(Application)).Should().BeSubsetOf(["Foundation", "Model", "Abstractions"],
            "handlers are written against contracts; the pipeline, the mediator and the adapters are chosen by the host");
    }

    [Theory]
    [InlineData(Domain)]
    [InlineData(Application)]
    [InlineData(Infrastructure)]
    public void OnlyTheApi_SeesHostPackages(string project)
    {
        Graph.Closure(project).Where(p => Tier(p) == "Host").Should().BeEmpty(
            $"{project} must not compose the process; ServiceDefaults, Presentation and the application pipeline belong to the Api");
    }

    [Fact]
    public void Infrastructure_SeesAdaptersButNoHost()
    {
        TiersOf(Graph.Closure(Infrastructure)).Should().BeSubsetOf(["Foundation", "Model", "Abstractions", "Adapter"]);
        Graph.DirectKernelPackages(Infrastructure).Select(Tier).Should().AllBe("Adapter",
            "Infrastructure adds adapters; contracts arrive through the Application");
    }

    [Fact]
    public void Api_IsTheCompositionRoot()
    {
        Graph.DirectKernelPackages(Api).Select(Tier).Should().AllBe("Host");
    }

    [Theory]
    [InlineData(Domain)]
    [InlineData(Application)]
    [InlineData(Infrastructure)]
    public void MediatR_IsNeverSeenBelowTheApi(string project)
    {
        Graph.Closure(project).Should().NotContain(p => p.StartsWith("MediatR", StringComparison.Ordinal),
            "MediatR is an implementation detail of SharedKernel.Application.Mediator.MediatR, which only the Api references");
    }

    [Theory]
    [InlineData(Domain)]
    [InlineData(Application)]
    [InlineData(Infrastructure)]
    [InlineData(Api)]
    public void ProductionProjects_NeverReferenceTestingPackages(string project)
    {
        Graph.Closure(project).Where(p => Tier(p) == "Testing").Should().BeEmpty();
    }

    [Fact]
    public void EveryKernelPackageInTheGraph_IsClassified()
    {
        var unknown = new[] { Domain, Application, Infrastructure, Api }
            .SelectMany(Graph.Closure)
            .Where(p => p.StartsWith("SharedKernel.", StringComparison.Ordinal) && !KernelTiers.ContainsKey(p) && !IsTestingPackage(p))
            .Distinct()
            .ToList();

        unknown.Should().BeEmpty("add every kernel package the service references to KernelTiers with its tier");
    }

    private static IEnumerable<string> TiersOf(IEnumerable<string> packages) =>
        packages.Where(p => p.StartsWith("SharedKernel.", StringComparison.Ordinal)).Select(Tier).Distinct();

    private static string Tier(string package) =>
        IsTestingPackage(package) ? "Testing"
        : KernelTiers.TryGetValue(package, out var tier) ? tier
        : package.StartsWith("SharedKernel.", StringComparison.Ordinal) ? "Unclassified"
        : "ThirdParty";

    private static bool IsTestingPackage(string package) =>
        package == "SharedKernel.Testing" || (package.StartsWith("SharedKernel.", StringComparison.Ordinal) && package.EndsWith(".Testing", StringComparison.Ordinal));

    /// <summary>
    /// The tier of every production kernel package, as each declares it in its own project file
    /// (<c>&lt;SharedKernelTier&gt;</c>). Testing packages are recognised by name.
    /// </summary>
    private static readonly Dictionary<string, string> KernelTiers = new(StringComparer.Ordinal)
    {
        // Foundation
        ["SharedKernel.Compression"] = "Foundation",
        ["SharedKernel.Configuration"] = "Foundation",
        ["SharedKernel.Core"] = "Foundation",
        ["SharedKernel.Cryptography"] = "Foundation",
        ["SharedKernel.DataPrivacy"] = "Foundation",
        ["SharedKernel.Execution"] = "Foundation",
        ["SharedKernel.FeatureManagement"] = "Foundation",
        ["SharedKernel.Localization"] = "Foundation",
        ["SharedKernel.Primitives"] = "Foundation",
        ["SharedKernel.Validation"] = "Foundation",

        // Model
        ["SharedKernel.Contracts"] = "Model",
        ["SharedKernel.Domain"] = "Model",

        // Abstractions
        ["SharedKernel.AI.Abstractions"] = "Abstractions",
        ["SharedKernel.Application"] = "Abstractions",
        ["SharedKernel.Caching.Abstractions"] = "Abstractions",
        ["SharedKernel.Idempotency.Abstractions"] = "Abstractions",
        ["SharedKernel.Integration.Notifications.Abstractions"] = "Abstractions",
        ["SharedKernel.Messaging.Abstractions"] = "Abstractions",
        ["SharedKernel.Persistence.Abstractions"] = "Abstractions",
        ["SharedKernel.Reporting.Abstractions"] = "Abstractions",
        ["SharedKernel.Search.Abstractions"] = "Abstractions",
        ["SharedKernel.Security.Abstractions"] = "Abstractions",
        ["SharedKernel.Storage.Abstractions"] = "Abstractions",

        // Adapter
        ["SharedKernel.AI.Qdrant"] = "Adapter",
        ["SharedKernel.AI.SemanticKernel"] = "Adapter",
        ["SharedKernel.Caching.FusionCache"] = "Adapter",
        ["SharedKernel.Caching.Redis"] = "Adapter",
        ["SharedKernel.Caching.Redis.Core"] = "Adapter",
        ["SharedKernel.Caching.Redis.DistributedLocking"] = "Adapter",
        ["SharedKernel.Caching.Redis.HashStore"] = "Adapter",
        ["SharedKernel.Caching.Redis.PubSub"] = "Adapter",
        ["SharedKernel.Communication"] = "Adapter",
        ["SharedKernel.Communication.Grpc"] = "Adapter",
        ["SharedKernel.Communication.Rest"] = "Adapter",
        ["SharedKernel.Cryptography.Argon2"] = "Adapter",
        ["SharedKernel.Cryptography.KeyVault.Azure"] = "Adapter",
        ["SharedKernel.Idempotency.EfCore"] = "Adapter",
        ["SharedKernel.Idempotency.Redis"] = "Adapter",
        ["SharedKernel.Integration.Notifications.Email.SendGrid"] = "Adapter",
        ["SharedKernel.Integration.Notifications.Sms.Twilio"] = "Adapter",
        ["SharedKernel.Integration.Webhooks"] = "Adapter",
        ["SharedKernel.Messaging.MassTransit"] = "Adapter",
        ["SharedKernel.Messaging.MassTransit.AzureServiceBus"] = "Adapter",
        ["SharedKernel.Messaging.MassTransit.EfCore"] = "Adapter",
        ["SharedKernel.Messaging.MassTransit.RabbitMq"] = "Adapter",
        ["SharedKernel.Persistence.Dapper"] = "Adapter",
        ["SharedKernel.Persistence.EfCore"] = "Adapter",
        ["SharedKernel.Persistence.EfCore.Auditing"] = "Adapter",
        ["SharedKernel.Persistence.EfCore.Encryption"] = "Adapter",
        ["SharedKernel.Persistence.Npgsql"] = "Adapter",
        ["SharedKernel.Reporting.Csv"] = "Adapter",
        ["SharedKernel.Reporting.Gotenberg"] = "Adapter",
        ["SharedKernel.Reporting.Pdf"] = "Adapter",
        ["SharedKernel.Reporting.Spreadsheet"] = "Adapter",
        ["SharedKernel.Scheduling"] = "Adapter",
        ["SharedKernel.Search.ElasticSearch"] = "Adapter",
        ["SharedKernel.Search.Meilisearch"] = "Adapter",
        ["SharedKernel.Storage.Obs"] = "Adapter",
        ["SharedKernel.Storage.S3"] = "Adapter",
        ["SharedKernel.Validation.FluentValidation"] = "Adapter",
        ["SharedKernel.Workflows.Temporal"] = "Adapter",

        // Host
        ["SharedKernel.Application.Mediator.MediatR"] = "Host",
        ["SharedKernel.Application.Pipeline"] = "Host",
        ["SharedKernel.Application.Pipeline.Caching"] = "Host",
        ["SharedKernel.MultiTenancy"] = "Host",
        ["SharedKernel.Presentation.Core"] = "Host",
        ["SharedKernel.Presentation.GraphQL"] = "Host",
        ["SharedKernel.Presentation.Grpc"] = "Host",
        ["SharedKernel.Presentation.OpenApi"] = "Host",
        ["SharedKernel.Presentation.SignalR"] = "Host",
        ["SharedKernel.Presentation.WebApi"] = "Host",
        ["SharedKernel.Security.ApiKey"] = "Host",
        ["SharedKernel.Security.Mtls"] = "Host",
        ["SharedKernel.Security.Oidc"] = "Host",
        ["SharedKernel.Security.Totp"] = "Host",
        ["SharedKernel.ServiceDefaults"] = "Host",
        ["SharedKernel.ServiceDefaults.Configuration.KeyVault"] = "Host",
        ["SharedKernel.ServiceDefaults.Localization"] = "Host",
        ["SharedKernel.ServiceDefaults.Persistence"] = "Host",
        ["SharedKernel.ServiceDefaults.Security"] = "Host",
        ["SharedKernel.ServiceDefaults.Security.Mtls"] = "Host",
    };

    /// <summary>The project and package graph recorded in this test assembly's <c>deps.json</c>.</summary>
    private sealed class DependencyGraph
    {
        private readonly Dictionary<string, string[]> _dependencies;
        private readonly HashSet<string> _projects;

        private DependencyGraph(Dictionary<string, string[]> dependencies, HashSet<string> projects)
        {
            _dependencies = dependencies;
            _projects = projects;
        }

        public static DependencyGraph Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "OrderApi.Tests.deps.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            var projects = root.GetProperty("libraries").EnumerateObject()
                .Where(l => l.Value.GetProperty("type").GetString() == "project")
                .Select(l => NameOf(l.Name))
                .ToHashSet(StringComparer.Ordinal);

            var target = root.GetProperty("targets").EnumerateObject().First().Value;
            var dependencies = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var library in target.EnumerateObject())
            {
                dependencies[NameOf(library.Name)] = library.Value.TryGetProperty("dependencies", out var deps)
                    ? deps.EnumerateObject().Select(d => d.Name).ToArray()
                    : [];
            }

            return new DependencyGraph(dependencies, projects);
        }

        public IEnumerable<string> Direct(string library) =>
            _dependencies.TryGetValue(library, out var deps)
                ? deps
                : throw new InvalidOperationException($"{library} is not in the dependency graph.");

        public IEnumerable<string> DirectProjects(string project) => Direct(project).Where(_projects.Contains);

        public IEnumerable<string> DirectKernelPackages(string project) =>
            Direct(project).Where(d => d.StartsWith("SharedKernel.", StringComparison.Ordinal));

        public IReadOnlySet<string> Closure(string library)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(Direct(library));
            while (pending.TryPop(out var next))
            {
                if (seen.Add(next) && _dependencies.TryGetValue(next, out var deps))
                {
                    foreach (var dep in deps)
                        pending.Push(dep);
                }
            }

            return seen;
        }

        private static string NameOf(string nameAndVersion) => nameAndVersion[..nameAndVersion.IndexOf('/')];
    }
}
