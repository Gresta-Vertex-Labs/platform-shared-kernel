using System.Reflection;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that mechanically enforce the <c>09.Search</c> two-provider
/// package topology (<c>SharedKernel.Search.Abstractions</c>, <c>SharedKernel.Search.Meilisearch</c>,
/// <c>SharedKernel.Search.ElasticSearch</c>) documented in prose by <c>09.Search/CLAUDE.md</c>.
///
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="StorageTopologyRules"/>'s structure and its documented
/// <c>NotHaveDependencyOn</c> matching contract exactly (namespace <c>StartsWith</c>, no trailing
/// dot, self-collision awareness) but is scoped to <c>09.Search</c>'s two SIBLING provider packages
/// (<c>SharedKernel.Search.Meilisearch</c>/<c>.ElasticSearch</c>, mirroring <c>08.Storage</c>'s
/// <c>.S3</c>/<c>.Obs</c> sibling-not-<c>.Core</c>-split precedent exactly, per
/// <c>09.Search/CLAUDE.md</c>'s own explicit rejection of a <c>SharedKernel.Search.Core</c>). No
/// Mono.Cecil, no <c>ICustomRule</c> — every check is a pure assembly-dependency-graph predicate
/// using <c>.Should().NotHaveDependencyOn(...)</c>, and no new SK diagnostic ID is introduced by
/// this class (SK0024/SK0025 are separate Roslyn analyzers; this class covers package topology
/// only).
/// </para>
/// <para>
/// <strong>Matching note:</strong> NetArchTest's <c>NotHaveDependencyOn(term)</c> compares
/// <c>term</c> against each scanned type's set of dependency <em>namespaces</em> using a
/// <c>StartsWith</c> comparison, with no trailing dot on either side. <c>"Meilisearch"</c> and
/// <c>"Elastic"</c> are deliberate bare prefixes — <c>"Meilisearch"</c> catches the MeiliSearch
/// SDK's root namespace, and <c>"Elastic"</c> catches every <c>Elastic.Clients.Elasticsearch</c>
/// namespace in one term — while <c>"SharedKernel.Search.Meilisearch"</c>/
/// <c>"SharedKernel.Search.ElasticSearch"</c>/<c>"SharedKernel.Configuration"</c>/
/// <c>"Microsoft.Extensions"</c> are exact package-identifying namespaces. None of the six terms is
/// a prefix of <c>"SharedKernel.Search.Abstractions"</c> — no self-collision for
/// <see cref="AbstractionsHasNoThirdPartyDependencies"/>.
/// </para>
/// <para>
/// <strong>Permitted exemption list</strong> (caller-controlled — carries no internal namespace
/// guard, consistent with <c>PresentationLayeringRules</c>/<c>CompositionRootExclusivityRules</c>/
/// <c>StorageTopologyRules</c>): none. Any future exemption must be documented in
/// <c>00.Governance/CLAUDE.md</c> before it is applied in code.
/// </para>
/// </remarks>
public static class SearchTopologyRules
{
    /// <summary>
    /// The six forbidden dependency terms for <c>SharedKernel.Search.Abstractions</c>.
    /// </summary>
    private static readonly string[] AbstractionsForbiddenTerms =
    [
        "Meilisearch",
        "Elastic",
        "SharedKernel.Search.Meilisearch",
        "SharedKernel.Search.ElasticSearch",
        "SharedKernel.Configuration",
        "Microsoft.Extensions",
    ];

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that <c>SharedKernel.Search.Abstractions</c>
    /// has no dependency on any of six forbidden terms: <c>"Meilisearch"</c> (bare prefix — catches
    /// the MeiliSearch SDK's root namespace), <c>"Elastic"</c> (bare prefix — catches every
    /// <c>Elastic.Clients.Elasticsearch</c> namespace), <c>"SharedKernel.Search.Meilisearch"</c>,
    /// <c>"SharedKernel.Search.ElasticSearch"</c>, <c>"SharedKernel.Configuration"</c> (the
    /// Options-validation package only the two provider packages need), or
    /// <c>"Microsoft.Extensions"</c> (the sixth, stricter term — <c>09.Search/CLAUDE.md</c> states
    /// Abstractions carries zero <c>PackageReference</c> entries of any kind, not even
    /// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> <c>09.Search/CLAUDE.md</c> documents
    /// <c>SharedKernel.Search.Abstractions</c> as having zero third-party NuGet dependencies and
    /// referencing only <c>SharedKernel.Primitives</c> (01.Core) and <c>SharedKernel.Contracts</c>
    /// (04.Contracts, for the guarded <c>ToPagedList()</c> bridge only) — the strictest dependency
    /// posture of any Abstractions package in the platform (stricter than
    /// <c>SharedKernel.Caching.Abstractions</c>, which is permitted
    /// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c>). This mechanically confirms the
    /// abstraction never accidentally couples to either concrete SDK, to either provider package, to
    /// the Options-validation package, or to any <c>Microsoft.Extensions.*</c> dependency at all.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.Search.Abstractions</c> references
    /// <c>MeiliSearch.MeilisearchClient</c> or <c>Elastic.Clients.Elasticsearch.ElasticsearchClient</c>
    /// directly on an interface member.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> <c>SharedKernel.Search.Abstractions</c> references only
    /// <c>SharedKernel.Primitives</c> (<c>Result</c>/<c>Result&lt;T&gt;</c>/<c>Error</c>),
    /// <c>SharedKernel.Contracts</c> (<c>PagedList&lt;T&gt;</c>), and BCL types.
    /// </para>
    /// </remarks>
    /// <param name="abstractionsAssembly">
    /// The <c>SharedKernel.Search.Abstractions</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInAbstractions).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList AbstractionsHasNoThirdPartyDependencies(Assembly abstractionsAssembly)
    {
        ConditionList conditionList = Types
            .InAssembly(abstractionsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(AbstractionsForbiddenTerms[0]);

        for (var i = 1; i < AbstractionsForbiddenTerms.Length; i++)
        {
            conditionList = conditionList.And().NotHaveDependencyOn(AbstractionsForbiddenTerms[i]);
        }

        return conditionList;
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> array of exactly two elements asserting that
    /// <c>SharedKernel.Search.Meilisearch</c> and <c>SharedKernel.Search.ElasticSearch</c> never
    /// reference each other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO NAMED <see cref="Assembly"/> parameters (not <c>params Assembly[]</c>) — mirroring
    /// <see cref="StorageTopologyRules.ProviderPackagesNeverReferenceEachOther"/>'s and
    /// <c>UnitOfWorkSeamRules</c>'s former <c>UnitOfWorkInterfacesRemainDistinct</c>'s two-named-parameter
    /// convention: the rule's whole purpose is comparing two specific, named packages, so
    /// positional <c>params</c> would obscure which assembly is expected to be which. Only two
    /// packages exist here and neither identifying namespace
    /// (<c>"SharedKernel.Search.Meilisearch"</c>, <c>"SharedKernel.Search.ElasticSearch"</c>) is a
    /// prefix of the other or of its own declaring assembly — no lookup table needed, unlike
    /// <see cref="RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther"/>'s four-package
    /// case.
    /// </para>
    /// <para>
    /// <strong>Rationale:</strong> <c>09.Search/CLAUDE.md</c>'s own Provider role note states
    /// explicitly that <c>SharedKernel.Search.Meilisearch</c> and
    /// <c>SharedKernel.Search.ElasticSearch</c> "are sibling <c>.{Provider}</c> packages, not a
    /// <c>.{Provider}.Core</c>/<c>.{Provider}.{Role}</c> split, and they must never reference each
    /// other" — shared implementation shape (the options-validation flow, the <c>SearchFilter</c>
    /// walker skeleton, receipt mapping, probe sequencing) is deliberately DUPLICATED rather than
    /// factored into a shared <c>SharedKernel.Search.Core</c>, mirroring the <c>08.Storage</c>
    /// <c>.S3</c>/<c>.Obs</c> precedent exactly.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.Search.ElasticSearch</c> references a
    /// type from <c>SharedKernel.Search.Meilisearch</c> (e.g., to reuse a constants class instead of
    /// independently declaring its own).
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> each provider package independently declares its own
    /// implementation shape, referencing only <c>SharedKernel.Search.Abstractions</c> and
    /// <c>SharedKernel.Configuration</c>.
    /// </para>
    /// </remarks>
    /// <param name="meilisearchAssembly">
    /// The <c>SharedKernel.Search.Meilisearch</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInMeilisearch).Assembly</c>.
    /// </param>
    /// <param name="elasticSearchAssembly">
    /// The <c>SharedKernel.Search.ElasticSearch</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInElasticSearch).Assembly</c>.
    /// </param>
    /// <returns>
    /// A two-element array: index 0 asserts <c>SharedKernel.Search.Meilisearch</c> has no
    /// dependency on <c>"SharedKernel.Search.ElasticSearch"</c>; index 1 asserts
    /// <c>SharedKernel.Search.ElasticSearch</c> has no dependency on
    /// <c>"SharedKernel.Search.Meilisearch"</c>. The caller must assert
    /// <c>.GetResult().IsSuccessful</c> on EACH element.
    /// </returns>
    public static ConditionList[] ProviderPackagesNeverReferenceEachOther(
        Assembly meilisearchAssembly,
        Assembly elasticSearchAssembly)
    {
        var meilisearchNeverReferencesElasticSearch = Types
            .InAssembly(meilisearchAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.Search.ElasticSearch");

        var elasticSearchNeverReferencesMeilisearch = Types
            .InAssembly(elasticSearchAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.Search.Meilisearch");

        return [meilisearchNeverReferencesElasticSearch, elasticSearchNeverReferencesMeilisearch];
    }
}
