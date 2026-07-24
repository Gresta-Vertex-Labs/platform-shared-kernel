using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Tests for SK0025 <see cref="ObsoleteElasticsearchClientUsageAnalyzer"/> — WO-044 P-278.
/// </summary>
/// <remarks>
/// <para>T-205: Fire path — <c>using Nest;</c> plus <c>Nest.ElasticClient</c> usage triggers
/// SK0025.</para>
/// <para>T-206: Fire path — a symbol resolving to the <c>Elasticsearch.Net</c> assembly triggers
/// SK0025.</para>
/// <para>T-207: Pass path — <c>Elastic.Clients.Elasticsearch</c> usage does not trigger SK0025.</para>
/// <para>
/// The rule's discriminator is <see cref="ISymbol.ContainingAssembly"/>.Name — resolved via the
/// SEPARATE-ASSEMBLY technique (<see cref="SolutionState.AdditionalProjects"/>, keyed by an
/// assembly name of literally <c>"NEST"</c> / <c>"Elasticsearch.Net"</c> /
/// <c>"Elastic.Clients.Elasticsearch"</c>), not the in-compilation-stub technique used elsewhere in
/// this project — an in-compilation stub would resolve to THIS TEST ASSEMBLY's own name, never to
/// the literal deprecated-package assembly name the rule actually checks. This is the first analyzer
/// test file in this project needing a genuinely separate compiled reference assembly with a
/// specific <see cref="System.Reflection.AssemblyName"/>.
/// </para>
/// </remarks>
public class SK0025_ObsoleteElasticsearchClientUsageAnalyzerTests
{
    private const string NestStubSource = """
        namespace Nest
        {
            public class ElasticClient
            {
                public ElasticClient()
                {
                }
            }

            public class ConnectionSettings
            {
            }

            public class QueryContainer
            {
            }
        }
        """;

    private const string ElasticsearchNetStubSource = """
        namespace Elasticsearch.Net
        {
            public class ElasticLowLevelClient
            {
            }
        }
        """;

    private const string ElasticClientsElasticsearchStubSource = """
        namespace Elastic.Clients.Elasticsearch
        {
            public class ElasticsearchClient
            {
                public ElasticsearchClient()
                {
                }
            }
        }
        """;

    /// <summary>
    /// Builds a <see cref="CSharpAnalyzerTest{TAnalyzer, TVerifier}"/> whose main compilation
    /// references a separately-compiled project named <paramref name="assemblyName"/> containing
    /// <paramref name="referencedProjectSource"/> — so
    /// <c>symbol.ContainingAssembly.Name == assemblyName</c> resolves exactly as it would for the
    /// real deprecated NuGet package.
    /// </summary>
    private static CSharpAnalyzerTest<ObsoleteElasticsearchClientUsageAnalyzer, DefaultVerifier> CreateTest(
        string mainSource,
        string assemblyName,
        string referencedProjectSource
    )
    {
        var test = new CSharpAnalyzerTest<ObsoleteElasticsearchClientUsageAnalyzer, DefaultVerifier>();
        test.TestState.Sources.Add(mainSource);

        var referencedProject = new ProjectState(
            assemblyName,
            LanguageNames.CSharp,
            $"/{assemblyName}/Test",
            ".cs"
        );
        referencedProject.Sources.Add(referencedProjectSource);
        test.TestState.AdditionalProjects.Add(assemblyName, referencedProject);
        test.TestState.AdditionalProjectReferences.Add(assemblyName);

        return test;
    }

    // ---------------------------------------------------------------------------
    // T-205 — Fire path: using Nest; plus Nest.ElasticClient usage
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_UsingNestDirectiveWithElasticClientUsage_ReportsSk0025()
    {
        var test = CreateTest(
            """
            using Nest;

            namespace Fixture.Search
            {
                public class LegacySearchClientFactory
                {
                    public void Create()
                    {
                        var client = new {|SK0025:ElasticClient|}();
                    }
                }
            }
            """,
            "NEST",
            NestStubSource
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_FullyQualifiedNestElasticClientUsage_ReportsSk0025()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                public class LegacySearchClientFactory
                {
                    public void Create()
                    {
                        var client = new Nest.{|SK0025:ElasticClient|}();
                    }
                }
            }
            """,
            "NEST",
            NestStubSource
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_NestConnectionSettingsUsage_ReportsSk0025()
    {
        var test = CreateTest(
            """
            using Nest;

            namespace Fixture.Search
            {
                public class LegacySearchClientFactory
                {
                    public void BuildSettings()
                    {
                        var settings = new {|SK0025:ConnectionSettings|}();
                    }
                }
            }
            """,
            "NEST",
            NestStubSource
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-206 — Fire path: a symbol resolving to the Elasticsearch.Net assembly
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_ElasticsearchNetLowLevelClientUsage_ReportsSk0025()
    {
        var test = CreateTest(
            """
            using Elasticsearch.Net;

            namespace Fixture.Search
            {
                public class LegacyLowLevelClientFactory
                {
                    public void Create()
                    {
                        var client = new {|SK0025:ElasticLowLevelClient|}();
                    }
                }
            }
            """,
            "Elasticsearch.Net",
            ElasticsearchNetStubSource
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-207 — Pass path: Elastic.Clients.Elasticsearch usage
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_ElasticClientsElasticsearchUsage_NoDiagnostic()
    {
        var test = CreateTest(
            """
            using Elastic.Clients.Elasticsearch;

            namespace Fixture.Search
            {
                public class SearchClientFactory
                {
                    public ElasticsearchClient Create() => new ElasticsearchClient();
                }
            }
            """,
            "Elastic.Clients.Elasticsearch",
            ElasticClientsElasticsearchStubSource
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_FullyQualifiedElasticClientsElasticsearchUsage_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Search
            {
                public class SearchClientFactory
                {
                    public Elastic.Clients.Elasticsearch.ElasticsearchClient Create() =>
                        new Elastic.Clients.Elasticsearch.ElasticsearchClient();
                }
            }
            """,
            "Elastic.Clients.Elasticsearch",
            ElasticClientsElasticsearchStubSource
        );
        await test.RunAsync();
    }
}
