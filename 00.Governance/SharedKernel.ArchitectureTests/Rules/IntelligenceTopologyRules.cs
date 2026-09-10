using System.Reflection;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that mechanically enforce the <c>10.Intelligence</c> package
/// topology (<c>SharedKernel.AI.Abstractions</c>, <c>SharedKernel.AI.Qdrant</c>,
/// <c>SharedKernel.AI.SemanticKernel</c>) documented in prose by <c>10.Intelligence/CLAUDE.md</c>.
///
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two-provider adaptation.</strong> The design originally scoped for
/// this class targeted THREE sibling providers — Qdrant, Milvus, SemanticKernel.
/// <c>SharedKernel.AI.Milvus</c> was permanently retracted before ever being built — verified
/// directly on disk: no <c>10.Intelligence/SharedKernel.AI.Milvus/</c> directory exists, and it
/// never will (<c>Milvus.Client</c> never shipped a stable release). <c>10.Intelligence</c> now
/// ships exactly TWO providers, so this class is implemented against that two-provider reality
/// rather than the originally-drafted three-provider shape: <see cref="ProviderPackagesNeverReferenceEachOther"/>
/// takes the TWO-named-Assembly-parameter form (mirroring
/// <see cref="StorageTopologyRules.ProviderPackagesNeverReferenceEachOther"/>/
/// <see cref="SearchTopologyRules.ProviderPackagesNeverReferenceEachOther"/> exactly), not the
/// three-named-parameter/six-element form the original design called for, and
/// <see cref="AbstractionsHasNoThirdPartyDependencies"/> carries SIX forbidden terms (dropping the
/// two Milvus terms — <c>"Milvus"</c> and <c>"SharedKernel.AI.Milvus"</c> — from the originally
/// drafted eight-term list), not eight.
/// </para>
/// <para>
/// Mirrors <see cref="StorageTopologyRules"/>/<see cref="SearchTopologyRules"/>'s structure and
/// documented <c>NotHaveDependencyOn</c> matching contract exactly (namespace <c>StartsWith</c>, no
/// trailing dot, self-collision awareness), per <c>10.Intelligence/CLAUDE.md</c>'s own explicit
/// statement that "no <c>.Core</c> is extracted to share implementation shape between them —
/// duplication ... is deliberate, mirroring the <c>09.Search</c> precedent exactly." No Mono.Cecil,
/// no <c>ICustomRule</c> — every check is a pure assembly-dependency-graph predicate using
/// <c>.Should().NotHaveDependencyOn(...)</c>, and no new SK diagnostic ID is introduced by this
/// class (SK0026/SK0027 are separate Roslyn analyzers; this class covers package topology only).
/// </para>
/// <para>
/// <strong>Matching note:</strong> <c>"Qdrant"</c> and <c>"Microsoft.SemanticKernel"</c> are
/// deliberate bare prefixes — <c>"Qdrant"</c> catches <c>Qdrant.Client</c>'s root namespace, and
/// <c>"Microsoft.SemanticKernel"</c> catches every <c>Microsoft.SemanticKernel.*</c> namespace in one
/// term — while <c>"SharedKernel.AI.Qdrant"</c>/<c>"SharedKernel.AI.SemanticKernel"</c>/
/// <c>"SharedKernel.Configuration"</c>/<c>"Microsoft.Extensions"</c> are exact package-identifying
/// namespaces. None of the six terms is a prefix of <c>"SharedKernel.AI.Abstractions"</c> — no
/// self-collision for <see cref="AbstractionsHasNoThirdPartyDependencies"/>.
/// </para>
/// <para>
/// <strong>Permitted exemption list</strong> (caller-controlled — carries no internal namespace
/// guard, consistent with <c>PresentationLayeringRules</c>/<c>CompositionRootExclusivityRules</c>/
/// <c>StorageTopologyRules</c>/<c>SearchTopologyRules</c>): none. Any future exemption must be
/// documented in <c>00.Governance/CLAUDE.md</c> before it is applied in code.
/// </para>
/// </remarks>
public static class IntelligenceTopologyRules
{
    /// <summary>
    /// The six forbidden dependency terms for <c>SharedKernel.AI.Abstractions</c> — the two-provider
    /// (Qdrant/SemanticKernel) set that survived that retraction.
    /// </summary>
    private static readonly string[] AbstractionsForbiddenTerms =
    [
        "Qdrant",
        "Microsoft.SemanticKernel",
        "SharedKernel.AI.Qdrant",
        "SharedKernel.AI.SemanticKernel",
        "SharedKernel.Configuration",
        "Microsoft.Extensions",
    ];

    /// <summary>
    /// The single forbidden dependency term for <see cref="NoHealthChecksDependencyAcrossIntelligencePackages"/>.
    /// </summary>
    private const string HealthChecksNamespace = "Microsoft.Extensions.Diagnostics.HealthChecks";

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that <c>SharedKernel.AI.Abstractions</c> has
    /// no dependency on any of six forbidden terms: <c>"Qdrant"</c> (bare prefix — catches
    /// <c>Qdrant.Client</c>'s root namespace), <c>"Microsoft.SemanticKernel"</c> (bare prefix —
    /// catches every <c>Microsoft.SemanticKernel.*</c> namespace), <c>"SharedKernel.AI.Qdrant"</c>,
    /// <c>"SharedKernel.AI.SemanticKernel"</c>, <c>"SharedKernel.Configuration"</c> (the
    /// Options-validation package only the two provider packages need), or
    /// <c>"Microsoft.Extensions"</c> (the stricter term — <c>10.Intelligence/CLAUDE.md</c>'s Hard
    /// Violations list states <c>.Abstractions</c> takes zero <c>PackageReference</c> beyond
    /// <c>SharedKernel.Primitives</c>/<c>.Contracts</c> <c>ProjectReference</c>s and ships no DI
    /// extension).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> mirrors <c>10.Intelligence/CLAUDE.md</c>'s own Hard Violations
    /// entry verbatim: "<c>SharedKernel.AI.Abstractions</c> taking a <c>PackageReference</c> that has
    /// not been explicitly adjudicated and recorded in this file. The default is zero."
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.AI.Abstractions</c> references
    /// <c>Qdrant.Client.QdrantClient</c> or <c>Microsoft.SemanticKernel.Kernel</c> directly on an
    /// interface member.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> <c>SharedKernel.AI.Abstractions</c> references only
    /// <c>SharedKernel.Primitives</c> (<c>Result</c>/<c>Result&lt;T&gt;</c>/<c>Error</c>) and, if
    /// genuinely needed, <c>SharedKernel.Contracts</c>.
    /// </para>
    /// </remarks>
    /// <param name="abstractionsAssembly">
    /// The <c>SharedKernel.AI.Abstractions</c> assembly under test — supply via
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
    /// <c>SharedKernel.AI.Qdrant</c> and <c>SharedKernel.AI.SemanticKernel</c> never reference each
    /// other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TWO NAMED <see cref="Assembly"/> parameters (not <c>params Assembly[]</c>) — the two-provider
    /// reality after one vector-database provider was retracted. The original design called for a
    /// THREE-named-Assembly-parameter form (Qdrant/Milvus/SemanticKernel) returning a six-element
    /// array; since <c>SharedKernel.AI.Milvus</c> was never built and never will be, this method
    /// mirrors <see cref="StorageTopologyRules.ProviderPackagesNeverReferenceEachOther"/>/
    /// <see cref="SearchTopologyRules.ProviderPackagesNeverReferenceEachOther"/>'s
    /// two-named-parameter convention instead: the rule's whole purpose is comparing two specific,
    /// named packages, so positional <c>params</c> would obscure which assembly is expected to be
    /// which. Neither identifying namespace (<c>"SharedKernel.AI.Qdrant"</c>,
    /// <c>"SharedKernel.AI.SemanticKernel"</c>) is a prefix of the other or of its own declaring
    /// assembly — no lookup table needed, unlike
    /// <see cref="RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther"/>'s four-package
    /// case.
    /// </para>
    /// <para>
    /// <strong>Rationale:</strong> <c>10.Intelligence/CLAUDE.md</c> states explicitly: "Sibling
    /// packages never reference each other, in any direction, at project or type level."
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> <c>SharedKernel.AI.SemanticKernel</c> references a type
    /// from <c>SharedKernel.AI.Qdrant</c> (e.g., to reuse a constants class instead of independently
    /// declaring its own).
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> each provider package independently declares its own
    /// implementation shape, referencing only <c>SharedKernel.AI.Abstractions</c> and
    /// <c>SharedKernel.Configuration</c>.
    /// </para>
    /// </remarks>
    /// <param name="qdrantAssembly">
    /// The <c>SharedKernel.AI.Qdrant</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInQdrant).Assembly</c>.
    /// </param>
    /// <param name="semanticKernelAssembly">
    /// The <c>SharedKernel.AI.SemanticKernel</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInSemanticKernel).Assembly</c>.
    /// </param>
    /// <returns>
    /// A two-element array: index 0 asserts <c>SharedKernel.AI.Qdrant</c> has no dependency on
    /// <c>"SharedKernel.AI.SemanticKernel"</c>; index 1 asserts
    /// <c>SharedKernel.AI.SemanticKernel</c> has no dependency on <c>"SharedKernel.AI.Qdrant"</c>.
    /// The caller must assert <c>.GetResult().IsSuccessful</c> on EACH element.
    /// </returns>
    public static ConditionList[] ProviderPackagesNeverReferenceEachOther(
        Assembly qdrantAssembly,
        Assembly semanticKernelAssembly)
    {
        var qdrantNeverReferencesSemanticKernel = Types
            .InAssembly(qdrantAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.AI.SemanticKernel");

        var semanticKernelNeverReferencesQdrant = Types
            .InAssembly(semanticKernelAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.AI.Qdrant");

        return [qdrantNeverReferencesSemanticKernel, semanticKernelNeverReferencesQdrant];
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied
    /// <paramref name="intelligenceAssemblies"/> has a dependency on
    /// <c>"Microsoft.Extensions.Diagnostics.HealthChecks"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately the NARROW full term, not the bare <c>"Microsoft.Extensions"</c> prefix
    /// <see cref="AbstractionsHasNoThirdPartyDependencies"/> uses — the two PROVIDER packages
    /// legitimately need OTHER <c>Microsoft.Extensions.*</c> packages (<c>DependencyInjection</c>,
    /// <c>Options</c>, <c>Logging</c>) for their DI wiring; only <c>SharedKernel.AI.Abstractions</c>
    /// itself carries the zero-<c>Microsoft.Extensions</c>-anything posture. Mirrors
    /// <c>StorageTopologyRules</c>'s/<c>SearchTopologyRules</c>'s sibling-precedent shape and, more
    /// directly, the narrow-term precedent this class's own sibling method for a later domain
    /// (<c>WorkflowTopologyRules.NoHealthChecksDependencyInWorkflows</c>) also follows.
    /// </para>
    /// <para>
    /// <strong>Rationale:</strong> mechanizes Domain Invariant #8 / <c>10.Intelligence/CLAUDE.md</c>'s
    /// own Hard Violations entry verbatim: "Implementing <c>IHealthCheck</c>, or referencing
    /// <c>Microsoft.Extensions.Diagnostics.HealthChecks</c>, anywhere in <c>10.Intelligence</c>." —
    /// <c>IVectorCollectionProvisioner.ProbeAsync</c> is the sanctioned readiness primitive; wiring
    /// into <c>AddHealthChecks()</c> remains a <c>13.ServiceDefaults</c> concern, mirroring the
    /// <c>06.Persistence</c>/<c>08.Storage</c>/<c>09.Search</c> readiness-probe split precedent.
    /// </para>
    /// </remarks>
    /// <param name="intelligenceAssemblies">
    /// The <c>10.Intelligence</c> production assemblies to check — typically
    /// <c>SharedKernel.AI.Abstractions</c>, <c>SharedKernel.AI.Qdrant</c>, and
    /// <c>SharedKernel.AI.SemanticKernel</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList NoHealthChecksDependencyAcrossIntelligencePackages(
        params Assembly[] intelligenceAssemblies) =>
        Types
            .InAssemblies(intelligenceAssemblies)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(HealthChecksNamespace);
}
