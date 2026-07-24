using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="SearchTopologyRules"/> — the <c>09.Search</c> two-provider package
/// topology enforcement predicates introduced by WO-044 P-278.
/// </summary>
/// <remarks>
/// <para>
/// T-208/T-209 cover <see cref="SearchTopologyRules.AbstractionsHasNoThirdPartyDependencies"/>.
/// T-210/T-211 cover <see cref="SearchTopologyRules.ProviderPackagesNeverReferenceEachOther"/>.
/// </para>
/// <para>
/// Every fire-path/pass-path pair uses CONTRIVED in-memory fixture assemblies — the primary
/// red/green proof this phase specifies, per the "designed-ahead-of-a-pending-dependency"
/// precedent this domain has followed since <c>RedisTopologyRulesTests</c>/
/// <c>StorageTopologyRulesTests</c>. Additional <c>Real*</c>-suffixed tests below re-run the same
/// predicates against the REAL, now Published <c>SharedKernel.Search.Abstractions</c>/
/// <c>.Meilisearch</c>/<c>.ElasticSearch</c> assemblies — the dependency this phase's own
/// acceptance criteria treat as GATING (unlike the non-blocking-follow-up shape of most prior
/// precedents) resolved to <c>●</c> Complete (139/139 tasks) before this phase's implementation
/// session, so real-assembly verification is wired now rather than deferred.
/// </para>
/// </remarks>
public class SearchTopologyRulesTests
{
    // ---------------------------------------------------------------------------
    // T-208 — Fire path: contrived Abstractions-shaped fixture depends on a stubbed forbidden type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-208: When a contrived "SharedKernel.Search.Abstractions"-shaped assembly references a
    /// type whose declaring assembly simulates the Meilisearch SDK's root namespace,
    /// <see cref="SearchTopologyRules.AbstractionsHasNoThirdPartyDependencies"/> must fail.
    /// </summary>
    [Fact]
    public void AbstractionsHasNoThirdPartyDependencies_ViolatingAssembly_RuleFails()
    {
        const string meilisearchStubSource = """
            namespace Meilisearch
            {
                public class MeilisearchClient { }
            }
            """;

        const string abstractionsSource = """
            namespace SharedKernel.Search.Abstractions.Abstractions
            {
                public class LeakySearchIndex
                {
                    private readonly Meilisearch.MeilisearchClient _client;
                    public LeakySearchIndex(Meilisearch.MeilisearchClient client)
                    {
                        _client = client;
                    }
                }
            }
            """;

        var meilisearchStubAssembly = CompileInMemory(
            "Fixture.SearchAbstractionsTest.Meilisearch",
            meilisearchStubSource);
        var abstractionsAssembly = CompileInMemory(
            "ViolatingSharedKernel.Search.Abstractions",
            abstractionsSource,
            extraReferences: new[] { meilisearchStubAssembly });

        var conditionList = SearchTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "LeakySearchIndex references the Meilisearch SDK namespace directly");
    }

    /// <summary>
    /// T-208 (Microsoft.Extensions variant): a contrived Abstractions-shaped fixture depending on
    /// a stubbed <c>Microsoft.Extensions.DependencyInjection</c> type must also fail — the SIXTH,
    /// stricter forbidden term this class carries beyond <c>StorageTopologyRules</c>'s five-term
    /// analog.
    /// </summary>
    [Fact]
    public void AbstractionsHasNoThirdPartyDependencies_MicrosoftExtensionsDependency_RuleFails()
    {
        const string diStubSource = """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
            }
            """;

        const string abstractionsSource = """
            namespace SharedKernel.Search.Abstractions.Abstractions
            {
                public class LeakyDiExtension
                {
                    public void Register(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    {
                    }
                }
            }
            """;

        var diStubAssembly = CompileInMemory(
            "Fixture.SearchAbstractionsDiTest.Microsoft.Extensions.DependencyInjection",
            diStubSource);
        var abstractionsAssembly = CompileInMemory(
            "ViolatingSharedKernel.Search.Abstractions.Di",
            abstractionsSource,
            extraReferences: new[] { diStubAssembly });

        var conditionList = SearchTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "SharedKernel.Search.Abstractions must carry zero Microsoft.Extensions.* dependency of any kind");
    }

    // ---------------------------------------------------------------------------
    // T-209 — Pass path: contrived Abstractions-shaped fixture depends only on stubbed Primitives
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-209: A contrived "SharedKernel.Search.Abstractions"-shaped assembly depending only on a
    /// stubbed <c>SharedKernel.Primitives</c> type must pass
    /// <see cref="SearchTopologyRules.AbstractionsHasNoThirdPartyDependencies"/>.
    /// </summary>
    [Fact]
    public void AbstractionsHasNoThirdPartyDependencies_CleanAssembly_RulePasses()
    {
        const string primitivesStubSource = """
            namespace SharedKernel.Primitives
            {
                public class Result { }
            }
            """;

        const string abstractionsSource = """
            namespace SharedKernel.Search.Abstractions.Abstractions
            {
                public class CleanSearchIndex
                {
                    public SharedKernel.Primitives.Result DoWork() => new SharedKernel.Primitives.Result();
                }
            }
            """;

        var primitivesStubAssembly = CompileInMemory(
            "Fixture.SearchAbstractionsCleanTest.SharedKernel.Primitives",
            primitivesStubSource);
        var abstractionsAssembly = CompileInMemory(
            "CleanSharedKernel.Search.Abstractions",
            abstractionsSource,
            extraReferences: new[] { primitivesStubAssembly });

        var conditionList = SearchTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "CleanSearchIndex depends only on SharedKernel.Primitives, never Meilisearch/Elastic/providers/Configuration/Microsoft.Extensions");
    }

    // ---------------------------------------------------------------------------
    // T-210 — Fire path: Meilisearch-shaped fixture references ElasticSearch-shaped fixture,
    // and vice versa
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-210: When a contrived "SharedKernel.Search.Meilisearch"-shaped assembly references a type
    /// whose declaring assembly simulates <c>SharedKernel.Search.ElasticSearch</c>,
    /// <see cref="SearchTopologyRules.ProviderPackagesNeverReferenceEachOther"/> must fail on
    /// array element [0] only.
    /// </summary>
    [Fact]
    public void ProviderPackagesNeverReferenceEachOther_MeilisearchReferencesElasticSearch_FirstElementFails()
    {
        const string elasticSearchStubSource = """
            namespace SharedKernel.Search.ElasticSearch
            {
                public interface IElasticSearchIndex { }
            }
            """;

        const string meilisearchSource = """
            namespace SharedKernel.Search.Meilisearch
            {
                public class LeakyMeilisearchIndex
                {
                    private readonly SharedKernel.Search.ElasticSearch.IElasticSearchIndex _elasticSearchIndex;
                    public LeakyMeilisearchIndex(SharedKernel.Search.ElasticSearch.IElasticSearchIndex elasticSearchIndex)
                    {
                        _elasticSearchIndex = elasticSearchIndex;
                    }
                }
            }
            """;

        const string cleanElasticSearchSource = """
            namespace SharedKernel.Search.ElasticSearch
            {
                public class CleanElasticSearchIndex { }
            }
            """;

        var elasticSearchStubAssembly = CompileInMemory(
            "Fixture.ProviderSiblingTest.SharedKernel.Search.ElasticSearch",
            elasticSearchStubSource);
        var violatingMeilisearchAssembly = CompileInMemory(
            "ViolatingSharedKernel.Search.Meilisearch",
            meilisearchSource,
            extraReferences: new[] { elasticSearchStubAssembly });
        var cleanElasticSearchAssembly = CompileInMemory(
            "CleanSharedKernel.Search.ElasticSearch",
            cleanElasticSearchSource);

        var conditionLists = SearchTopologyRules.ProviderPackagesNeverReferenceEachOther(
            violatingMeilisearchAssembly,
            cleanElasticSearchAssembly);

        conditionLists.Should().HaveCount(2);

        conditionLists[0].GetResult().IsSuccessful.Should().BeFalse(
            because: "LeakyMeilisearchIndex references SharedKernel.Search.ElasticSearch directly");
        conditionLists[1].GetResult().IsSuccessful.Should().BeTrue(
            because: "the clean ElasticSearch fixture has no dependency on SharedKernel.Search.Meilisearch");
    }

    /// <summary>
    /// T-210 (converse): When a contrived "SharedKernel.Search.ElasticSearch"-shaped assembly
    /// references a type whose declaring assembly simulates <c>SharedKernel.Search.Meilisearch</c>,
    /// <see cref="SearchTopologyRules.ProviderPackagesNeverReferenceEachOther"/> must fail on
    /// array element [1] only.
    /// </summary>
    [Fact]
    public void ProviderPackagesNeverReferenceEachOther_ElasticSearchReferencesMeilisearch_SecondElementFails()
    {
        const string meilisearchStubSource = """
            namespace SharedKernel.Search.Meilisearch
            {
                public interface IMeilisearchIndex { }
            }
            """;

        const string elasticSearchSource = """
            namespace SharedKernel.Search.ElasticSearch
            {
                public class LeakyElasticSearchIndex
                {
                    private readonly SharedKernel.Search.Meilisearch.IMeilisearchIndex _meilisearchIndex;
                    public LeakyElasticSearchIndex(SharedKernel.Search.Meilisearch.IMeilisearchIndex meilisearchIndex)
                    {
                        _meilisearchIndex = meilisearchIndex;
                    }
                }
            }
            """;

        const string cleanMeilisearchSource = """
            namespace SharedKernel.Search.Meilisearch
            {
                public class CleanMeilisearchIndex { }
            }
            """;

        var meilisearchStubAssembly = CompileInMemory(
            "Fixture.ProviderSiblingConverseTest.SharedKernel.Search.Meilisearch",
            meilisearchStubSource);
        var violatingElasticSearchAssembly = CompileInMemory(
            "ViolatingSharedKernel.Search.ElasticSearch",
            elasticSearchSource,
            extraReferences: new[] { meilisearchStubAssembly });
        var cleanMeilisearchAssembly = CompileInMemory(
            "CleanSharedKernel.Search.Meilisearch",
            cleanMeilisearchSource);

        var conditionLists = SearchTopologyRules.ProviderPackagesNeverReferenceEachOther(
            cleanMeilisearchAssembly,
            violatingElasticSearchAssembly);

        conditionLists.Should().HaveCount(2);

        conditionLists[0].GetResult().IsSuccessful.Should().BeTrue(
            because: "the clean Meilisearch fixture has no dependency on SharedKernel.Search.ElasticSearch");
        conditionLists[1].GetResult().IsSuccessful.Should().BeFalse(
            because: "LeakyElasticSearchIndex references SharedKernel.Search.Meilisearch directly");
    }

    // ---------------------------------------------------------------------------
    // T-211 — Pass path: Meilisearch/ElasticSearch fixtures with no cross-reference pass both
    // array elements
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-211: Contrived "SharedKernel.Search.Meilisearch"- and "SharedKernel.Search.ElasticSearch"-
    /// shaped assemblies with no cross-reference must pass both array elements of
    /// <see cref="SearchTopologyRules.ProviderPackagesNeverReferenceEachOther"/>.
    /// </summary>
    [Fact]
    public void ProviderPackagesNeverReferenceEachOther_NoCrossReference_BothElementsPass()
    {
        const string meilisearchSource = """
            namespace SharedKernel.Search.Meilisearch
            {
                public class CleanMeilisearchIndex { }
            }
            """;

        const string elasticSearchSource = """
            namespace SharedKernel.Search.ElasticSearch
            {
                public class CleanElasticSearchIndex { }
            }
            """;

        var meilisearchAssembly = CompileInMemory("CleanSharedKernel.Search.Meilisearch.NoCross", meilisearchSource);
        var elasticSearchAssembly = CompileInMemory("CleanSharedKernel.Search.ElasticSearch.NoCross", elasticSearchSource);

        var conditionLists = SearchTopologyRules.ProviderPackagesNeverReferenceEachOther(
            meilisearchAssembly,
            elasticSearchAssembly);

        conditionLists.Should().HaveCount(2);
        foreach (var conditionList in conditionLists)
        {
            conditionList.GetResult().IsSuccessful.Should().BeTrue(
                because: "neither provider package references the other");
        }
    }

    // ---------------------------------------------------------------------------
    // Real-assembly verification — 09.Search reached Published (139/139 tasks, all P-272/P-273/
    // P-274 shipped) before this phase's implementation session; the contrived fixtures above
    // remain the primary red/green proof per the phase spec, but the real assemblies are
    // ADDITIONALLY verified here since the phase's own acceptance criteria treat this as GATING.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Real-assembly verification: the actual <c>SharedKernel.Search.Abstractions</c> assembly
    /// (P-272) has zero third-party dependencies — confirms the contrived-fixture proof (T-208/
    /// T-209) generalizes to the shipped package.
    /// </summary>
    [Fact]
    public void AbstractionsHasNoThirdPartyDependencies_RealAbstractionsAssembly_RulePasses()
    {
        var abstractionsAssembly = typeof(Search.Abstractions.Abstractions.ISearchDocument).Assembly;

        var conditionList = SearchTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real SharedKernel.Search.Abstractions references only SharedKernel.Primitives and SharedKernel.Contracts");
    }

    /// <summary>
    /// Real-assembly verification: the actual <c>SharedKernel.Search.Meilisearch</c> (P-273) and
    /// <c>SharedKernel.Search.ElasticSearch</c> (P-274) assemblies never reference each other.
    /// </summary>
    [Fact]
    public void ProviderPackagesNeverReferenceEachOther_RealMeilisearchAndElasticSearchAssemblies_BothElementsPass()
    {
        var meilisearchAssembly = typeof(Search.Meilisearch.Options.MeilisearchOptions).Assembly;
        var elasticSearchAssembly = typeof(Search.ElasticSearch.Options.ElasticSearchOptions).Assembly;

        var conditionLists = SearchTopologyRules.ProviderPackagesNeverReferenceEachOther(
            meilisearchAssembly,
            elasticSearchAssembly);

        conditionLists.Should().HaveCount(2);
        foreach (var conditionList in conditionLists)
        {
            conditionList.GetResult().IsSuccessful.Should().BeTrue(
                because: "SharedKernel.Search.Meilisearch and SharedKernel.Search.ElasticSearch are independently-declared siblings");
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly and loads it for reflection.
    /// </summary>
    /// <remarks>
    /// References supplied via <paramref name="extraReferences"/> are turned into
    /// <see cref="MetadataReference"/>s from their in-memory image
    /// (<see cref="MetadataReference.CreateFromImage(System.Collections.Immutable.ImmutableArray{byte})"/>)
    /// rather than from <see cref="Assembly.Location"/> — the same technique documented for
    /// <c>RedisTopologyRulesTests</c>/<c>StorageTopologyRulesTests</c>, avoiding CS0234 failures
    /// when chaining fixtures compiled in the same test run.
    /// </remarks>
    private static Assembly CompileInMemory(
        string assemblyName,
        string source,
        Assembly[]? extraReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        if (extraReferences is not null)
        {
            foreach (var extraReference in extraReferences)
            {
                references.Add(
                    MetadataReference.CreateFromImage(
                        System.Collections.Immutable.ImmutableArray.Create(
                            File.ReadAllBytes(extraReference.Location))));
            }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{assemblyName}_{System.Guid.NewGuid():N}.dll");

        using (var stream = new MemoryStream())
        {
            var emitResult = compilation.Emit(stream);
            if (!emitResult.Success)
            {
                var errors = string.Join(
                    System.Environment.NewLine,
                    emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                throw new InvalidOperationException(
                    $"Fixture '{assemblyName}' failed to compile:{System.Environment.NewLine}{errors}");
            }

            stream.Seek(0, SeekOrigin.Begin);
            File.WriteAllBytes(tempPath, stream.ToArray());
        }

        return Assembly.LoadFrom(tempPath);
    }
}
