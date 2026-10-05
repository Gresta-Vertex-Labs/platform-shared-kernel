using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0027 <see cref="RawIntelligenceIdentifierLiteralAnalyzer"/> — WO-045 P-286.</summary>
/// <remarks>
/// <para>T-219: Fire path — <c>VectorFilter.Eq("field", value)</c> raw literal triggers SK0027.</para>
/// <para>
/// T-220: Fire path — <c>IVectorCollectionProvisioner.ProbeAsync("collection-name", ct)</c> raw
/// literal triggers SK0027.
/// </para>
/// <para>
/// T-221: Fire path — <c>VectorCollectionDefinition.Create("name", "embedding-model-id", ...)</c>
/// triggers SK0027 independently on BOTH the <c>name</c> and <c>embeddingModelId</c> literal
/// arguments.
/// </para>
/// <para>
/// T-222: Pass path — <c>VectorFilter.Eq(nameof(ProductChunkRecord.Category), value)</c> does not
/// trigger SK0027.
/// </para>
/// <para>
/// T-223: Pass path — <c>VectorCollectionDefinitionBuilder.EmbeddingModel(IntelligenceModelIds.ProductEmbeddingV1, 1536)</c>
/// (domain-local identifier-constants class reference) does not trigger SK0027.
/// </para>
/// <para>
/// Fixture stubs mirror the REAL shipped <c>SharedKernel.AI.Abstractions</c> source (namespaces
/// <c>SharedKernel.AI.Abstractions.Models</c> and <c>SharedKernel.AI.Abstractions.Abstractions</c>,
/// confirmed against <c>src/Infrastructure/AI/SharedKernel.AI.Abstractions/Models/VectorFilter.cs</c>,
/// <c>Models/VectorCollectionDefinition.cs</c>, <c>Models/VectorCollectionDefinitionBuilder.cs</c>,
/// and <c>Abstractions/IVectorCollectionProvisioner.cs</c>) — an in-compilation stub, not a
/// <c>ProjectReference</c>, per the SK0017/SK0022/SK0024 precedent of self-contained analyzer test
/// fixtures.
/// </para>
/// </remarks>
public class SK0027_RawIntelligenceIdentifierLiteralAnalyzerTests
{
    private const string AbstractionsStubs = """
        namespace SharedKernel.AI.Abstractions.Models
        {
            public readonly struct VectorValue
            {
                public static implicit operator VectorValue(string value) => default;
                public static implicit operator VectorValue(long value) => default;
            }

            public enum VectorFieldKind
            {
                String = 0,
                Int64 = 1,
                Double = 2,
                Boolean = 3,
                DateTimeOffset = 4,
            }

            public enum VectorDistanceMetric
            {
                Cosine = 0,
                DotProduct = 1,
                Euclidean = 2,
            }

            public sealed class VectorFieldDefinition
            {
                public string Name { get; set; } = "";
                public VectorFieldKind Kind { get; set; }
                public bool Filterable { get; set; }
            }

            public abstract class VectorFilter
            {
                public static VectorFilter Eq(string field, VectorValue value) => null!;
                public static VectorFilter Ne(string field, VectorValue value) => null!;
                public static VectorFilter In(string field, params VectorValue[] values) => null!;
                public static VectorFilter Between(
                    string field,
                    VectorValue? from,
                    VectorValue? to,
                    bool fromInclusive = true,
                    bool toInclusive = true) => null!;
                public static VectorFilter Exists(string field) => null!;
            }

            public sealed class VectorCollectionDefinition
            {
                public static VectorCollectionDefinition Create(
                    string name,
                    string embeddingModelId,
                    int dimension,
                    VectorDistanceMetric metric,
                    System.Collections.Generic.IReadOnlyList<VectorFieldDefinition> fields) => null!;
            }

            public sealed class VectorCollectionDefinitionBuilder
            {
                public VectorCollectionDefinitionBuilder(string name)
                {
                }

                public VectorCollectionDefinitionBuilder EmbeddingModel(string modelId, int dimension) => this;
                public VectorCollectionDefinitionBuilder DistanceMetric(VectorDistanceMetric metric) => this;
                public VectorCollectionDefinitionBuilder Field(string name, VectorFieldKind kind, bool filterable = false) => this;
            }
        }

        namespace SharedKernel.AI.Abstractions.Abstractions
        {
            public interface IVectorCollectionProvisioner
            {
                System.Threading.Tasks.Task<bool> CollectionExistsAsync(
                    string collectionName,
                    System.Threading.CancellationToken cancellationToken = default);

                System.Threading.Tasks.Task DeleteCollectionAsync(
                    string collectionName,
                    System.Threading.CancellationToken cancellationToken = default);

                System.Threading.Tasks.Task ProbeAsync(
                    string collectionName,
                    System.Threading.CancellationToken cancellationToken = default);
            }
        }

        """;

    private const string FixtureStub = """
        namespace Fixture.Intelligence
        {
            public sealed class ProductChunkRecord
            {
                public string Category => "widgets";
            }

            public static class IntelligenceModelIds
            {
                public const string ProductEmbeddingV1 = "product-embedding-v1";
            }
        }

        """;

    private static CSharpAnalyzerTest<RawIntelligenceIdentifierLiteralAnalyzer, DefaultVerifier> CreateTest(
        string source) =>
        new()
        {
            TestCode = AbstractionsStubs + FixtureStub + source,
        };

    // ---------------------------------------------------------------------------
    // T-219 — Fire path: VectorFilter.Eq("field", value)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_VectorFilterEqRawLiteral_ReportsSk0027()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using SharedKernel.AI.Abstractions.Models;

                public class ProductFilters
                {
                    public VectorFilter CategoryEquals(VectorValue value) =>
                        VectorFilter.Eq({|SK0027:"Category"|}, value);
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_VectorFilterNeRawLiteral_ReportsSk0027()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using SharedKernel.AI.Abstractions.Models;

                public class ProductFilters
                {
                    public VectorFilter CategoryNotEquals(VectorValue value) =>
                        VectorFilter.Ne({|SK0027:"Category"|}, value);
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_VectorFilterInRawLiteral_ReportsSk0027()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using SharedKernel.AI.Abstractions.Models;

                public class ProductFilters
                {
                    public VectorFilter CategoryIn(VectorValue a, VectorValue b) =>
                        VectorFilter.In({|SK0027:"Category"|}, a, b);
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_VectorFilterBetweenRawLiteral_ReportsSk0027()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using SharedKernel.AI.Abstractions.Models;

                public class ProductFilters
                {
                    public VectorFilter PriceBetween(long from, long to) =>
                        VectorFilter.Between({|SK0027:"Price"|}, from, to);
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_VectorFilterExistsRawLiteral_ReportsSk0027()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using SharedKernel.AI.Abstractions.Models;

                public class ProductFilters
                {
                    public VectorFilter HasDiscount() =>
                        VectorFilter.Exists({|SK0027:"DiscountPercent"|});
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-220 — Fire path: IVectorCollectionProvisioner.ProbeAsync("collection-name", ct)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_ProvisionerProbeAsyncRawLiteral_ReportsSk0027()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using System.Threading;
                using System.Threading.Tasks;
                using SharedKernel.AI.Abstractions.Abstractions;

                public class ProvisionerCaller
                {
                    public Task ProbeProducts(IVectorCollectionProvisioner provisioner, CancellationToken ct) =>
                        provisioner.ProbeAsync({|SK0027:"products"|}, ct);
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_ProvisionerCollectionExistsAsyncRawLiteral_ReportsSk0027()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using System.Threading;
                using System.Threading.Tasks;
                using SharedKernel.AI.Abstractions.Abstractions;

                public class ProvisionerCaller
                {
                    public Task<bool> ExistsProducts(IVectorCollectionProvisioner provisioner, CancellationToken ct) =>
                        provisioner.CollectionExistsAsync({|SK0027:"products"|}, ct);
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_ProvisionerDeleteCollectionAsyncRawLiteral_ReportsSk0027()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using System.Threading;
                using System.Threading.Tasks;
                using SharedKernel.AI.Abstractions.Abstractions;

                public class ProvisionerCaller
                {
                    public Task DeleteProducts(IVectorCollectionProvisioner provisioner, CancellationToken ct) =>
                        provisioner.DeleteCollectionAsync({|SK0027:"products"|}, ct);
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-221 — Fire path: VectorCollectionDefinition.Create("name", "embedding-model-id", ...)
    //         triggers independently on BOTH literal arguments
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_DefinitionCreateBothRawLiterals_ReportsSk0027OnBoth()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using SharedKernel.AI.Abstractions.Models;

                public class ProductCollections
                {
                    public VectorCollectionDefinition Create() =>
                        VectorCollectionDefinition.Create(
                            {|SK0027:"products"|},
                            {|SK0027:"product-embedding-v1"|},
                            1536,
                            VectorDistanceMetric.Cosine,
                            new VectorFieldDefinition[0]);
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // Fire path — VectorCollectionDefinitionBuilder.EmbeddingModel / .Field
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_BuilderEmbeddingModelRawLiteral_ReportsSk0027()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using SharedKernel.AI.Abstractions.Models;

                public class ProductCollections
                {
                    public VectorCollectionDefinitionBuilder Configure(VectorCollectionDefinitionBuilder builder) =>
                        builder.EmbeddingModel({|SK0027:"product-embedding-v1"|}, 1536);
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_BuilderFieldRawLiteral_ReportsSk0027()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using SharedKernel.AI.Abstractions.Models;

                public class ProductCollections
                {
                    public VectorCollectionDefinitionBuilder Configure(VectorCollectionDefinitionBuilder builder) =>
                        builder.Field({|SK0027:"Category"|}, VectorFieldKind.String, filterable: true);
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-222 — Pass path: VectorFilter.Eq(nameof(ProductChunkRecord.Category), value)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_VectorFilterEqWithNameof_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using SharedKernel.AI.Abstractions.Models;

                public class ProductFilters
                {
                    public VectorFilter CategoryEquals(VectorValue value) =>
                        VectorFilter.Eq(nameof(ProductChunkRecord.Category), value);
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-223 — Pass path: VectorCollectionDefinitionBuilder.EmbeddingModel(IntelligenceModelIds.ProductEmbeddingV1, 1536)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_BuilderEmbeddingModelWithIdentifierConstantsClass_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Intelligence
            {
                using SharedKernel.AI.Abstractions.Models;

                public class ProductCollections
                {
                    public VectorCollectionDefinitionBuilder Configure(VectorCollectionDefinitionBuilder builder) =>
                        builder.EmbeddingModel(IntelligenceModelIds.ProductEmbeddingV1, 1536);
                }
            }
            """
        );
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path proving the declaring-type discriminator this rule requires: an unrelated
    /// <c>Eq(string, object)</c>-shaped method on a type outside
    /// <c>SharedKernel.AI.Abstractions.Models</c> must never fire SK0027.
    /// </summary>
    [Fact]
    public async Task PassPath_UnrelatedEqMethodOnDifferentType_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Unrelated
            {
                public class ReportFilters
                {
                    public static ReportFilters Eq(string field, object value) => new();
                }

                public class ReportQueries
                {
                    public ReportFilters Build() => ReportFilters.Eq("Title", 1);
                }
            }
            """
        );
        await test.RunAsync();
    }
}
