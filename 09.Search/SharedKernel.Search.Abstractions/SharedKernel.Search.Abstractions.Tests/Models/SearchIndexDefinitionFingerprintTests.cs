using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Tests.Models;

/// <summary>
/// T-06: <see cref="SearchIndexDefinition.ComputeFingerprint()"/> canonicalisation tests — declaration-order
/// independence (ordinal field sort), sensitivity to every individual role/kind/name/tenant-field/
/// ceiling change, output shape (64 lowercase hex characters), and a pinned golden-value test locking
/// the exact canonical-string format so a future refactor cannot silently change it.
/// </summary>
/// <remarks>
/// The golden value below was produced by running the shipped <see cref="SearchIndexDefinition"/>
/// implementation itself against the definition in <see cref="BaselineDefinition"/> — not hand-computed
/// — so this test locks current behaviour rather than asserting a guessed value. An undefined
/// canonicalisation would either report a mismatch on every deploy or never detect real drift; both
/// silently defeat the readiness gate <c>ProbeAsync</c> depends on.
/// </remarks>
public sealed class SearchIndexDefinitionFingerprintTests
{
    private const string GoldenFingerprint = "405acb590750c1b919f0844238d022f5674ac11861752b16a4c6f62f0ad49173";

    private static SearchIndexDefinition BaselineDefinition(
        IReadOnlyList<SearchFieldDefinition>? fieldsInDeclarationOrder = null) => new()
    {
        Name = "products",
        TenantField = "tenantId",
        Fields = fieldsInDeclarationOrder ??
        [
            new SearchFieldDefinition { Name = "name", Kind = SearchFieldKind.Text, Searchable = true },
            new SearchFieldDefinition { Name = "price", Kind = SearchFieldKind.Decimal, Filterable = true, Sortable = true },
            new SearchFieldDefinition { Name = "tenantId", Kind = SearchFieldKind.Keyword, Filterable = true },
        ],
    };

    [Fact]
    public void GoldenValue_MatchesThePinnedCanonicalFormat()
    {
        BaselineDefinition().ComputeFingerprint().Should().Be(GoldenFingerprint);
    }

    [Fact]
    public void Fingerprint_Is64LowercaseHexCharacters()
    {
        var fingerprint = BaselineDefinition().ComputeFingerprint();

        fingerprint.Should().HaveLength(64);
        fingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Fingerprint_IsStable_AcrossFieldDeclarationOrder()
    {
        var declaredNameFirst = BaselineDefinition(
        [
            new SearchFieldDefinition { Name = "name", Kind = SearchFieldKind.Text, Searchable = true },
            new SearchFieldDefinition { Name = "price", Kind = SearchFieldKind.Decimal, Filterable = true, Sortable = true },
            new SearchFieldDefinition { Name = "tenantId", Kind = SearchFieldKind.Keyword, Filterable = true },
        ]);

        var declaredTenantFirst = BaselineDefinition(
        [
            new SearchFieldDefinition { Name = "tenantId", Kind = SearchFieldKind.Keyword, Filterable = true },
            new SearchFieldDefinition { Name = "price", Kind = SearchFieldKind.Decimal, Filterable = true, Sortable = true },
            new SearchFieldDefinition { Name = "name", Kind = SearchFieldKind.Text, Searchable = true },
        ]);

        declaredNameFirst.ComputeFingerprint().Should().Be(declaredTenantFirst.ComputeFingerprint());
        declaredNameFirst.ComputeFingerprint().Should().Be(GoldenFingerprint);
    }

    [Fact]
    public void Fingerprint_Changes_WhenAFieldRoleBooleanChanges()
    {
        var withSearchable = BaselineDefinition() with
        {
            Fields =
            [
                new SearchFieldDefinition { Name = "name", Kind = SearchFieldKind.Text, Searchable = true, Filterable = true },
            ],
        };
        var withoutFilterable = BaselineDefinition() with
        {
            Fields =
            [
                new SearchFieldDefinition { Name = "name", Kind = SearchFieldKind.Text, Searchable = true, Filterable = false },
            ],
        };

        withSearchable.ComputeFingerprint().Should().NotBe(withoutFilterable.ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_Changes_WhenAFieldKindChanges()
    {
        var asText = BaselineDefinition() with
        {
            Fields = [new SearchFieldDefinition { Name = "f", Kind = SearchFieldKind.Text }],
        };
        var asKeyword = BaselineDefinition() with
        {
            Fields = [new SearchFieldDefinition { Name = "f", Kind = SearchFieldKind.Keyword }],
        };

        asText.ComputeFingerprint().Should().NotBe(asKeyword.ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_Changes_WhenAFieldNameChanges()
    {
        var fieldA = BaselineDefinition() with
        {
            Fields = [new SearchFieldDefinition { Name = "a", Kind = SearchFieldKind.Text }],
        };
        var fieldB = BaselineDefinition() with
        {
            Fields = [new SearchFieldDefinition { Name = "b", Kind = SearchFieldKind.Text }],
        };

        fieldA.ComputeFingerprint().Should().NotBe(fieldB.ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_Changes_WhenTenantFieldChanges()
    {
        var withTenant = BaselineDefinition() with { TenantField = "tenantId" };
        var withoutTenant = BaselineDefinition() with { TenantField = null };
        var differentTenant = BaselineDefinition() with { TenantField = "orgId" };

        withTenant.ComputeFingerprint().Should().NotBe(withoutTenant.ComputeFingerprint());
        withTenant.ComputeFingerprint().Should().NotBe(differentTenant.ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_Changes_WhenMaxTotalHitsChanges()
    {
        var default1000 = BaselineDefinition() with { MaxTotalHits = 1000 };
        var custom5000 = BaselineDefinition() with { MaxTotalHits = 5000 };

        default1000.ComputeFingerprint().Should().NotBe(custom5000.ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_Changes_WhenMaxFacetValuesChanges()
    {
        var default100 = BaselineDefinition() with { MaxFacetValues = 100 };
        var custom50 = BaselineDefinition() with { MaxFacetValues = 50 };

        default100.ComputeFingerprint().Should().NotBe(custom50.ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_Changes_WhenIndexNameChanges()
    {
        var productsIndex = BaselineDefinition() with { Name = "products" };
        var ordersIndex = BaselineDefinition() with { Name = "orders" };

        productsIndex.ComputeFingerprint().Should().NotBe(ordersIndex.ComputeFingerprint());
    }

    [Fact]
    public void Fingerprint_Changes_WhenPrimaryKeyFieldChanges()
    {
        var defaultKey = BaselineDefinition() with { PrimaryKeyField = "documentId" };
        var customKey = BaselineDefinition() with { PrimaryKeyField = "sku" };

        defaultKey.ComputeFingerprint().Should().NotBe(customKey.ComputeFingerprint());
    }
}
