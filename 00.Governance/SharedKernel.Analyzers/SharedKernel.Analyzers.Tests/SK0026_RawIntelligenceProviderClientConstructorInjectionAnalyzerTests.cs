using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0026 <see cref="RawIntelligenceProviderClientConstructorInjectionAnalyzer"/> — WO-045 P-286.</summary>
/// <remarks>
/// <para>T-214: Fire path — <c>QdrantClient</c> injected in a constructor outside
/// <c>SharedKernel.AI.Qdrant</c> triggers SK0026.</para>
/// <para>
/// T-215 (Milvus fire-path test) is DROPPED per the WO-048 adaptation — <c>SharedKernel.AI.Milvus</c>
/// was permanently retracted before ever being built and the platform no longer recognizes
/// <c>Milvus.Client.MilvusClient</c> as a resolved client type.
/// </para>
/// <para>T-216: Fire path — <c>Microsoft.SemanticKernel.Kernel</c> injected in a constructor outside
/// <c>SharedKernel.AI.SemanticKernel</c> triggers SK0026.</para>
/// <para>T-217: Pass path — <c>QdrantClient</c> injected inside a
/// <c>SharedKernel.AI.Qdrant</c>-namespaced class does not trigger SK0026.</para>
/// <para>
/// T-218: Pass path — an unrelated user-defined type also named <c>Kernel</c> (different namespace,
/// not <c>Microsoft.SemanticKernel.Kernel</c>) injected anywhere does not trigger SK0026 — proves the
/// semantic-model exact-type resolution avoids the simple-name collision risk.
/// </para>
/// <para>
/// Fixture stubs are in-compilation stand-ins for the real <c>Qdrant.Client.QdrantClient</c> and
/// <c>Microsoft.SemanticKernel.Kernel</c> types (neither package is referenced by this test project),
/// the same in-compilation-stub technique already established by SK0013/SK0017/SK0022/SK0024. Every
/// per-test parameter type reference uses the <c>global::</c>-qualified form (e.g.
/// <c>global::Qdrant.Client.QdrantClient</c>) rather than a <c>using</c> directive, for two reasons
/// confirmed empirically while authoring this file: (1) a <c>using</c> directive placed after the
/// stub namespaces already declared earlier in the same compilation unit is invalid — using
/// directives must precede every namespace-member-declaration in a compilation unit; and (2) even a
/// syntactically valid nested <c>using Qdrant.Client;</c> inside a
/// <c>namespace SharedKernel.AI.Qdrant.*</c> block resolves incorrectly (a genuine CS0234) because
/// the bare leading "Qdrant" segment binds against the enclosing "SharedKernel.AI.Qdrant"
/// namespace's own trailing "Qdrant" segment instead of the global "Qdrant" root namespace. The
/// <c>global::</c> qualifier sidesteps both problems and is textually irrelevant to the analyzer's
/// own semantic-model type resolution, which resolves the symbol regardless of how the source
/// spelled the reference.
/// </para>
/// </remarks>
public class SK0026_RawIntelligenceProviderClientConstructorInjectionAnalyzerTests
{
    private const string ClientStubs = """
        namespace Qdrant.Client
        {
            public class QdrantClient
            {
            }
        }

        namespace Microsoft.SemanticKernel
        {
            public class Kernel
            {
            }
        }

        """;

    private static CSharpAnalyzerTest<RawIntelligenceProviderClientConstructorInjectionAnalyzer, DefaultVerifier> CreateTest(
        string source) =>
        new()
        {
            TestCode = ClientStubs + source,
        };

    // ---------------------------------------------------------------------------
    // T-214 — Fire path: QdrantClient injected outside SharedKernel.AI.Qdrant
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_QdrantClientInjectedOutsideOwningPackage_ReportsSk0026()
    {
        var test = CreateTest(
            """
            namespace Fixture.Consumer
            {
                public class OrderVectorRepository
                {
                    public OrderVectorRepository({|SK0026:global::Qdrant.Client.QdrantClient|} client)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-216 — Fire path: Microsoft.SemanticKernel.Kernel injected outside SharedKernel.AI.SemanticKernel
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_SemanticKernelKernelInjectedOutsideOwningPackage_ReportsSk0026()
    {
        var test = CreateTest(
            """
            namespace Fixture.Consumer
            {
                public class OrderSummarizer
                {
                    public OrderSummarizer({|SK0026:global::Microsoft.SemanticKernel.Kernel|} kernel)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-217 — Pass path: QdrantClient injected inside SharedKernel.AI.Qdrant namespace
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_QdrantClientInjectedInsideOwningPackage_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace SharedKernel.AI.Qdrant.Collections
            {
                public class QdrantVectorCollection
                {
                    public QdrantVectorCollection(global::Qdrant.Client.QdrantClient client)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path — <c>Kernel</c> injected inside <c>SharedKernel.AI.SemanticKernel</c> namespace does
    /// not trigger SK0026, the SemanticKernel-side counterpart to T-217.
    /// </summary>
    [Fact]
    public async Task PassPath_SemanticKernelKernelInjectedInsideOwningPackage_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace SharedKernel.AI.SemanticKernel.Orchestration
            {
                public class SemanticKernelOrchestrator
                {
                    public SemanticKernelOrchestrator(global::Microsoft.SemanticKernel.Kernel kernel)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path — a <c>QdrantClient</c> parameter injected inside
    /// <c>SharedKernel.AI.SemanticKernel</c> STILL fires, since there is no cross-exemption between
    /// owning packages (the sibling-package violation topology is independently caught at the
    /// assembly level by <c>IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther</c>).
    /// </summary>
    [Fact]
    public async Task FirePath_QdrantClientInjectedInsideDifferentOwningPackage_ReportsSk0026()
    {
        var test = CreateTest(
            """
            namespace SharedKernel.AI.SemanticKernel.Orchestration
            {
                public class CrossProviderHelper
                {
                    public CrossProviderHelper({|SK0026:global::Qdrant.Client.QdrantClient|} client)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-218 — Pass path: unrelated user-defined type also named Kernel
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_UnrelatedKernelTypeFromDifferentNamespace_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Imaging
            {
                public class Kernel
                {
                }

                public class ConvolutionProcessor
                {
                    public ConvolutionProcessor(Kernel kernel)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }
}
