using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="IntelligenceTopologyRules"/> — the <c>10.Intelligence</c> package topology
/// enforcement predicates introduced by WO-045 P-286.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two-provider adaptation (WO-048, 2026-07-27):</strong> these tests exercise the
/// two-provider (Qdrant/SemanticKernel) reality <see cref="IntelligenceTopologyRules"/> is actually
/// implemented against — <c>SharedKernel.AI.Milvus</c> was permanently retracted before ever being
/// built, so there is no Milvus fixture, no Milvus fire-path test, and
/// <see cref="IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther"/> is exercised as a
/// two-element array, not the originally-drafted six-element/three-package matrix.
/// </para>
/// <para>
/// T-224/T-225 cover <see cref="IntelligenceTopologyRules.AbstractionsHasNoThirdPartyDependencies"/>.
/// T-226/T-228 cover <see cref="IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther"/>
/// (T-227, the original three-way-matrix-beyond-two-way-check test, is not applicable in the
/// two-provider reality and is dropped). T-229/T-230 cover
/// <see cref="IntelligenceTopologyRules.NoHealthChecksDependencyAcrossIntelligencePackages"/>.
/// </para>
/// <para>
/// Every fire-path/pass-path pair uses CONTRIVED in-memory fixture assemblies — the primary
/// red/green proof, per the "designed-ahead-of-a-pending-dependency" precedent this domain has
/// followed since <c>RedisTopologyRulesTests</c>/<c>StorageTopologyRulesTests</c>/
/// <c>SearchTopologyRulesTests</c>. Additional <c>Real*</c>-suffixed tests below re-run the same
/// predicates against the REAL, now-Published <c>SharedKernel.AI.Abstractions</c>/<c>.Qdrant</c>/
/// <c>.SemanticKernel</c> assemblies — this phase's own acceptance criteria treat real-assembly
/// verification as GATING, and <c>10.Intelligence</c> reached Published (all six phases <c>●</c>,
/// WO-048) before this phase's implementation session, so real-assembly verification is wired now
/// rather than deferred.
/// </para>
/// </remarks>
public class IntelligenceTopologyRulesTests
{
    // ---------------------------------------------------------------------------
    // T-224 — Fire path: contrived Abstractions-shaped fixture depends on a stubbed forbidden type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-224: When a contrived "SharedKernel.AI.Abstractions"-shaped assembly references a type
    /// whose declaring assembly simulates the Qdrant SDK's root namespace,
    /// <see cref="IntelligenceTopologyRules.AbstractionsHasNoThirdPartyDependencies"/> must fail.
    /// </summary>
    [Fact]
    public void AbstractionsHasNoThirdPartyDependencies_QdrantDependency_RuleFails()
    {
        const string qdrantStubSource = """
            namespace Qdrant.Client
            {
                public class QdrantClient { }
            }
            """;

        const string abstractionsSource = """
            namespace SharedKernel.AI.Abstractions.Abstractions
            {
                public class LeakyVectorCollection
                {
                    private readonly Qdrant.Client.QdrantClient _client;
                    public LeakyVectorCollection(Qdrant.Client.QdrantClient client)
                    {
                        _client = client;
                    }
                }
            }
            """;

        var qdrantStubAssembly = CompileInMemory(
            "Fixture.IntelligenceAbstractionsTest.Qdrant.Client",
            qdrantStubSource);
        var abstractionsAssembly = CompileInMemory(
            "ViolatingSharedKernel.AI.Abstractions.Qdrant",
            abstractionsSource,
            extraReferences: new[] { qdrantStubAssembly });

        var conditionList = IntelligenceTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "LeakyVectorCollection references the Qdrant SDK namespace directly");
    }

    /// <summary>
    /// T-224 (SemanticKernel variant): a contrived Abstractions-shaped fixture depending on a
    /// stubbed <c>Microsoft.SemanticKernel</c> type must also fail.
    /// </summary>
    [Fact]
    public void AbstractionsHasNoThirdPartyDependencies_SemanticKernelDependency_RuleFails()
    {
        const string semanticKernelStubSource = """
            namespace Microsoft.SemanticKernel
            {
                public class Kernel { }
            }
            """;

        const string abstractionsSource = """
            namespace SharedKernel.AI.Abstractions.Abstractions
            {
                public class LeakyOrchestrator
                {
                    private readonly Microsoft.SemanticKernel.Kernel _kernel;
                    public LeakyOrchestrator(Microsoft.SemanticKernel.Kernel kernel)
                    {
                        _kernel = kernel;
                    }
                }
            }
            """;

        var semanticKernelStubAssembly = CompileInMemory(
            "Fixture.IntelligenceAbstractionsSkTest.Microsoft.SemanticKernel",
            semanticKernelStubSource);
        var abstractionsAssembly = CompileInMemory(
            "ViolatingSharedKernel.AI.Abstractions.SemanticKernel",
            abstractionsSource,
            extraReferences: new[] { semanticKernelStubAssembly });

        var conditionList = IntelligenceTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "LeakyOrchestrator references the Microsoft.SemanticKernel namespace directly");
    }

    /// <summary>
    /// T-224 (Microsoft.Extensions variant): a contrived Abstractions-shaped fixture depending on
    /// a stubbed <c>Microsoft.Extensions.DependencyInjection</c> type must also fail — the strict,
    /// zero-<c>Microsoft.Extensions</c>-anything posture <c>src/Infrastructure/AI/CLAUDE.md</c> documents.
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
            namespace SharedKernel.AI.Abstractions.Abstractions
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
            "Fixture.IntelligenceAbstractionsDiTest.Microsoft.Extensions.DependencyInjection",
            diStubSource);
        var abstractionsAssembly = CompileInMemory(
            "ViolatingSharedKernel.AI.Abstractions.Di",
            abstractionsSource,
            extraReferences: new[] { diStubAssembly });

        var conditionList = IntelligenceTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "SharedKernel.AI.Abstractions must carry zero Microsoft.Extensions.* dependency of any kind");
    }

    // ---------------------------------------------------------------------------
    // T-225 — Pass path: contrived Abstractions-shaped fixture depends only on stubbed Primitives
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-225: A contrived "SharedKernel.AI.Abstractions"-shaped assembly depending only on a
    /// stubbed <c>SharedKernel.Primitives</c> type must pass
    /// <see cref="IntelligenceTopologyRules.AbstractionsHasNoThirdPartyDependencies"/>.
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
            namespace SharedKernel.AI.Abstractions.Abstractions
            {
                public class CleanVectorCollection
                {
                    public SharedKernel.Primitives.Result DoWork() => new SharedKernel.Primitives.Result();
                }
            }
            """;

        var primitivesStubAssembly = CompileInMemory(
            "Fixture.IntelligenceAbstractionsCleanTest.SharedKernel.Primitives",
            primitivesStubSource);
        var abstractionsAssembly = CompileInMemory(
            "CleanSharedKernel.AI.Abstractions",
            abstractionsSource,
            extraReferences: new[] { primitivesStubAssembly });

        var conditionList = IntelligenceTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "CleanVectorCollection depends only on SharedKernel.Primitives, never Qdrant/SemanticKernel/providers/Configuration/Microsoft.Extensions");
    }

    // ---------------------------------------------------------------------------
    // T-226 — Fire path: Qdrant-shaped fixture references SemanticKernel-shaped fixture
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-226: When a contrived "SharedKernel.AI.Qdrant"-shaped assembly references a type whose
    /// declaring assembly simulates <c>SharedKernel.AI.SemanticKernel</c>,
    /// <see cref="IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther"/> must fail on
    /// array element [0] only.
    /// </summary>
    [Fact]
    public void ProviderPackagesNeverReferenceEachOther_QdrantReferencesSemanticKernel_FirstElementFails()
    {
        const string semanticKernelStubSource = """
            namespace SharedKernel.AI.SemanticKernel
            {
                public interface ISemanticKernelOrchestrator { }
            }
            """;

        const string qdrantSource = """
            namespace SharedKernel.AI.Qdrant
            {
                public class LeakyQdrantCollection
                {
                    private readonly SharedKernel.AI.SemanticKernel.ISemanticKernelOrchestrator _orchestrator;
                    public LeakyQdrantCollection(SharedKernel.AI.SemanticKernel.ISemanticKernelOrchestrator orchestrator)
                    {
                        _orchestrator = orchestrator;
                    }
                }
            }
            """;

        const string cleanSemanticKernelSource = """
            namespace SharedKernel.AI.SemanticKernel
            {
                public class CleanSemanticKernelOrchestrator { }
            }
            """;

        var semanticKernelStubAssembly = CompileInMemory(
            "Fixture.IntelligenceProviderSiblingTest.SharedKernel.AI.SemanticKernel",
            semanticKernelStubSource);
        var violatingQdrantAssembly = CompileInMemory(
            "ViolatingSharedKernel.AI.Qdrant",
            qdrantSource,
            extraReferences: new[] { semanticKernelStubAssembly });
        var cleanSemanticKernelAssembly = CompileInMemory(
            "CleanSharedKernel.AI.SemanticKernel",
            cleanSemanticKernelSource);

        var conditionLists = IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther(
            violatingQdrantAssembly,
            cleanSemanticKernelAssembly);

        conditionLists.Should().HaveCount(2);

        conditionLists[0].GetResult().IsSuccessful.Should().BeFalse(
            because: "LeakyQdrantCollection references SharedKernel.AI.SemanticKernel directly");
        conditionLists[1].GetResult().IsSuccessful.Should().BeTrue(
            because: "the clean SemanticKernel fixture has no dependency on SharedKernel.AI.Qdrant");
    }

    /// <summary>
    /// T-226 (converse): When a contrived "SharedKernel.AI.SemanticKernel"-shaped assembly
    /// references a type whose declaring assembly simulates <c>SharedKernel.AI.Qdrant</c>,
    /// <see cref="IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther"/> must fail on
    /// array element [1] only.
    /// </summary>
    [Fact]
    public void ProviderPackagesNeverReferenceEachOther_SemanticKernelReferencesQdrant_SecondElementFails()
    {
        const string qdrantStubSource = """
            namespace SharedKernel.AI.Qdrant
            {
                public interface IQdrantVectorCollection { }
            }
            """;

        const string semanticKernelSource = """
            namespace SharedKernel.AI.SemanticKernel
            {
                public class LeakySemanticKernelOrchestrator
                {
                    private readonly SharedKernel.AI.Qdrant.IQdrantVectorCollection _collection;
                    public LeakySemanticKernelOrchestrator(SharedKernel.AI.Qdrant.IQdrantVectorCollection collection)
                    {
                        _collection = collection;
                    }
                }
            }
            """;

        const string cleanQdrantSource = """
            namespace SharedKernel.AI.Qdrant
            {
                public class CleanQdrantVectorCollection { }
            }
            """;

        var qdrantStubAssembly = CompileInMemory(
            "Fixture.IntelligenceProviderSiblingConverseTest.SharedKernel.AI.Qdrant",
            qdrantStubSource);
        var violatingSemanticKernelAssembly = CompileInMemory(
            "ViolatingSharedKernel.AI.SemanticKernel",
            semanticKernelSource,
            extraReferences: new[] { qdrantStubAssembly });
        var cleanQdrantAssembly = CompileInMemory(
            "CleanSharedKernel.AI.Qdrant",
            cleanQdrantSource);

        var conditionLists = IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther(
            cleanQdrantAssembly,
            violatingSemanticKernelAssembly);

        conditionLists.Should().HaveCount(2);

        conditionLists[0].GetResult().IsSuccessful.Should().BeTrue(
            because: "the clean Qdrant fixture has no dependency on SharedKernel.AI.SemanticKernel");
        conditionLists[1].GetResult().IsSuccessful.Should().BeFalse(
            because: "LeakySemanticKernelOrchestrator references SharedKernel.AI.Qdrant directly");
    }

    // ---------------------------------------------------------------------------
    // T-228 — Pass path: Qdrant/SemanticKernel fixtures with no cross-reference pass both array
    // elements
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-228: Contrived "SharedKernel.AI.Qdrant"- and "SharedKernel.AI.SemanticKernel"-shaped
    /// assemblies with no cross-reference must pass both array elements of
    /// <see cref="IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther"/>.
    /// </summary>
    [Fact]
    public void ProviderPackagesNeverReferenceEachOther_NoCrossReference_BothElementsPass()
    {
        const string qdrantSource = """
            namespace SharedKernel.AI.Qdrant
            {
                public class CleanQdrantVectorCollection { }
            }
            """;

        const string semanticKernelSource = """
            namespace SharedKernel.AI.SemanticKernel
            {
                public class CleanSemanticKernelOrchestrator { }
            }
            """;

        var qdrantAssembly = CompileInMemory("CleanSharedKernel.AI.Qdrant.NoCross", qdrantSource);
        var semanticKernelAssembly = CompileInMemory("CleanSharedKernel.AI.SemanticKernel.NoCross", semanticKernelSource);

        var conditionLists = IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther(
            qdrantAssembly,
            semanticKernelAssembly);

        conditionLists.Should().HaveCount(2);
        foreach (var conditionList in conditionLists)
        {
            conditionList.GetResult().IsSuccessful.Should().BeTrue(
                because: "neither provider package references the other");
        }
    }

    // ---------------------------------------------------------------------------
    // T-229 — Fire path: a contrived 10.Intelligence-shaped fixture references a stubbed
    // Microsoft.Extensions.Diagnostics.HealthChecks type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-229: When a contrived <c>10.Intelligence</c>-shaped assembly references a type whose
    /// declaring assembly simulates <c>Microsoft.Extensions.Diagnostics.HealthChecks</c>,
    /// <see cref="IntelligenceTopologyRules.NoHealthChecksDependencyAcrossIntelligencePackages"/>
    /// must fail.
    /// </summary>
    [Fact]
    public void NoHealthChecksDependencyAcrossIntelligencePackages_HealthChecksDependency_RuleFails()
    {
        const string healthChecksStubSource = """
            namespace Microsoft.Extensions.Diagnostics.HealthChecks
            {
                public interface IHealthCheck { }
            }
            """;

        const string qdrantSource = """
            namespace SharedKernel.AI.Qdrant
            {
                public class LeakyHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
                {
                }
            }
            """;

        var healthChecksStubAssembly = CompileInMemory(
            "Fixture.IntelligenceHealthChecksTest.Microsoft.Extensions.Diagnostics.HealthChecks",
            healthChecksStubSource);
        var qdrantAssembly = CompileInMemory(
            "ViolatingSharedKernel.AI.Qdrant.HealthChecks",
            qdrantSource,
            extraReferences: new[] { healthChecksStubAssembly });

        var conditionList = IntelligenceTopologyRules.NoHealthChecksDependencyAcrossIntelligencePackages(qdrantAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "LeakyHealthCheck implements Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck directly");
    }

    // ---------------------------------------------------------------------------
    // T-230 — Pass path: a contrived fixture references another Microsoft.Extensions.* namespace
    // but not HealthChecks
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-230: A contrived <c>10.Intelligence</c>-shaped assembly referencing
    /// <c>Microsoft.Extensions.DependencyInjection</c> (a legitimately-needed provider-package
    /// dependency) but not <c>Microsoft.Extensions.Diagnostics.HealthChecks</c> must pass
    /// <see cref="IntelligenceTopologyRules.NoHealthChecksDependencyAcrossIntelligencePackages"/>.
    /// </summary>
    [Fact]
    public void NoHealthChecksDependencyAcrossIntelligencePackages_OtherMicrosoftExtensionsDependency_RulePasses()
    {
        const string diStubSource = """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
            }
            """;

        const string qdrantSource = """
            namespace SharedKernel.AI.Qdrant
            {
                public class QdrantServiceCollectionExtensions
                {
                    public void Register(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    {
                    }
                }
            }
            """;

        var diStubAssembly = CompileInMemory(
            "Fixture.IntelligenceHealthChecksCleanTest.Microsoft.Extensions.DependencyInjection",
            diStubSource);
        var qdrantAssembly = CompileInMemory(
            "CleanSharedKernel.AI.Qdrant.HealthChecks",
            qdrantSource,
            extraReferences: new[] { diStubAssembly });

        var conditionList = IntelligenceTopologyRules.NoHealthChecksDependencyAcrossIntelligencePackages(qdrantAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "QdrantServiceCollectionExtensions depends on DependencyInjection, not HealthChecks");
    }

    // ---------------------------------------------------------------------------
    // Real-assembly verification — 10.Intelligence reached Published (all six phases ●, WO-048)
    // before this phase's implementation session; the contrived fixtures above remain the primary
    // red/green proof per the phase spec, but the real assemblies are ADDITIONALLY verified here
    // since the phase's own acceptance criteria treat this as GATING.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Real-assembly verification: the actual <c>SharedKernel.AI.Abstractions</c> assembly has zero
    /// third-party dependencies — confirms the contrived-fixture proof (T-224/T-225) generalizes to
    /// the shipped package.
    /// </summary>
    [Fact]
    public void AbstractionsHasNoThirdPartyDependencies_RealAbstractionsAssembly_RulePasses()
    {
        var abstractionsAssembly = typeof(AI.Abstractions.Abstractions.IVectorCollectionProvisioner).Assembly;

        var conditionList = IntelligenceTopologyRules.AbstractionsHasNoThirdPartyDependencies(abstractionsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real SharedKernel.AI.Abstractions references only SharedKernel.Primitives");
    }

    /// <summary>
    /// Real-assembly verification: the actual <c>SharedKernel.AI.Qdrant</c> and
    /// <c>SharedKernel.AI.SemanticKernel</c> assemblies never reference each other.
    /// </summary>
    [Fact]
    public void ProviderPackagesNeverReferenceEachOther_RealQdrantAndSemanticKernelAssemblies_BothElementsPass()
    {
        var qdrantAssembly = typeof(AI.Qdrant.Options.QdrantOptions).Assembly;
        var semanticKernelAssembly = typeof(AI.SemanticKernel.Options.SemanticKernelOptions).Assembly;

        var conditionLists = IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther(
            qdrantAssembly,
            semanticKernelAssembly);

        conditionLists.Should().HaveCount(2);
        foreach (var conditionList in conditionLists)
        {
            conditionList.GetResult().IsSuccessful.Should().BeTrue(
                because: "SharedKernel.AI.Qdrant and SharedKernel.AI.SemanticKernel are independently-declared siblings");
        }
    }

    /// <summary>
    /// Real-assembly verification: none of the real <c>SharedKernel.AI.Abstractions</c>/
    /// <c>.Qdrant</c>/<c>.SemanticKernel</c> assemblies references
    /// <c>Microsoft.Extensions.Diagnostics.HealthChecks</c> — confirms Domain Invariant #8 holds
    /// against the shipped packages.
    /// </summary>
    [Fact]
    public void NoHealthChecksDependencyAcrossIntelligencePackages_RealIntelligenceAssemblies_RulePasses()
    {
        var abstractionsAssembly = typeof(AI.Abstractions.Abstractions.IVectorCollectionProvisioner).Assembly;
        var qdrantAssembly = typeof(AI.Qdrant.Options.QdrantOptions).Assembly;
        var semanticKernelAssembly = typeof(AI.SemanticKernel.Options.SemanticKernelOptions).Assembly;

        var conditionList = IntelligenceTopologyRules.NoHealthChecksDependencyAcrossIntelligencePackages(
            abstractionsAssembly,
            qdrantAssembly,
            semanticKernelAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "10.Intelligence ships no IHealthCheck implementation and references Microsoft.Extensions.Diagnostics.HealthChecks nowhere");
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
    /// <c>RedisTopologyRulesTests</c>/<c>StorageTopologyRulesTests</c>/<c>SearchTopologyRulesTests</c>,
    /// avoiding CS0234 failures when chaining fixtures compiled in the same test run.
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
