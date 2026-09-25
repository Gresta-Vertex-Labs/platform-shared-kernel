using System.Reflection;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest layering rules for the SharedKernel architecture.
/// Each factory method corresponds 1:1 to a constraint in the root CLAUDE.md layering table.
/// </summary>
/// <remarks>
/// Use these in your architecture test project with a given assembly:
/// <code>
/// var result = SharedKernelLayeringRules.DomainNeverReferencesPersistence(domainAssembly).GetResult();
/// AssertRule(conditionList);
/// </code>
/// </remarks>
public static class SharedKernelLayeringRules
{
    // Namespace constants matching the SharedKernel package convention
    private const string CachingNamespace = "SharedKernel.Caching";
    private const string DomainNamespace = "SharedKernel.Domain";
    private const string ContractsNamespace = "SharedKernel.Contracts";
    private const string PersistenceNamespace = "SharedKernel.Persistence";
    private const string MessagingNamespace = "SharedKernel.Messaging";
    private const string StorageNamespace = "SharedKernel.Storage";
    private const string SearchNamespace = "SharedKernel.Search";
    private const string TestingNamespace = "SharedKernel.Testing";

    /// <summary>The packable consumer-facing persistence test helpers (16.Testing, P-558): test projects only.</summary>
    public const string PersistenceTestingNamespace = "SharedKernel.Persistence.Testing";

    /// <summary>
    /// 01.Core — references nothing. Core types must not depend on any other SharedKernel domain.
    /// </summary>
    /// <param name="assembly">The Core assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Core types have no SharedKernel dependencies.</returns>
    public static ConditionList CoreReferencesNothing(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(CachingNamespace)
            .And()
            .NotHaveDependencyOn(DomainNamespace)
            .And()
            .NotHaveDependencyOn(ContractsNamespace)
            .And()
            .NotHaveDependencyOn(PersistenceNamespace)
            .And()
            .NotHaveDependencyOn(MessagingNamespace);

    /// <summary>
    /// 02.Caching — may only reference 01.Core. Must not reference Domain, Contracts, or any
    /// infrastructure layer.
    /// </summary>
    /// <param name="assembly">The Caching assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Caching types do not reference layers above Core.</returns>
    public static ConditionList CachingReferencesOnlyCore(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(DomainNamespace)
            .And()
            .NotHaveDependencyOn(ContractsNamespace)
            .And()
            .NotHaveDependencyOn(PersistenceNamespace)
            .And()
            .NotHaveDependencyOn(MessagingNamespace);

    /// <summary>
    /// 03.Domain — may only reference 01.Core. Must not reference Caching, Contracts, or infrastructure.
    /// </summary>
    /// <param name="assembly">The Domain assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Domain types only reference Core.</returns>
    public static ConditionList DomainReferencesOnlyCore(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(CachingNamespace)
            .And()
            .NotHaveDependencyOn(ContractsNamespace)
            .And()
            .NotHaveDependencyOn(PersistenceNamespace)
            .And()
            .NotHaveDependencyOn(MessagingNamespace);

    /// <summary>
    /// 04.Contracts — may reference 01.Core only. Must not reference 03.Domain, Caching, or any
    /// infrastructure layer.
    /// </summary>
    /// <remarks>
    /// <c>SharedKernel.Domain</c> is forbidden because a wire contract is an independent, versioned
    /// projection of a domain model, never the model itself: a contract that references domain types
    /// changes shape whenever the model does, and drags the domain package into every consumer.
    /// <c>SharedKernel.Primitives</c> (<c>Error</c>, <c>ValidationResult&lt;T&gt;</c>) stays permitted.
    /// </remarks>
    /// <param name="assembly">The Contracts assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Contracts types reference no layer other than Core.</returns>
    public static ConditionList ContractsReferencesOnlyCore(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(DomainNamespace)
            .And()
            .NotHaveDependencyOn(CachingNamespace)
            .And()
            .NotHaveDependencyOn(PersistenceNamespace)
            .And()
            .NotHaveDependencyOn(MessagingNamespace)
            .And()
            .NotHaveDependencyOn(StorageNamespace)
            .And()
            .NotHaveDependencyOn(SearchNamespace);

    /// <summary>
    /// Hard rule: 03.Domain must never reference 06.Persistence or any persistence infrastructure.
    /// Violating this rule would couple domain logic to storage concerns.
    /// </summary>
    /// <param name="assembly">The Domain assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Domain types have no dependency on Persistence.</returns>
    public static ConditionList DomainNeverReferencesPersistence(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(PersistenceNamespace);

    /// <summary>
    /// Hard rule: 03.Domain must never reference 07.Messaging or any messaging infrastructure.
    /// Domain events are raised by the domain — never dispatched directly from it.
    /// </summary>
    /// <param name="assembly">The Domain assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Domain types have no dependency on Messaging.</returns>
    public static ConditionList DomainNeverReferencesMessaging(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(MessagingNamespace);

    /// <summary>
    /// Hard rule: 05.Application must never reference concrete infrastructure packages.
    /// Application logic depends on abstractions only (interfaces, not implementations).
    /// </summary>
    /// <param name="assembly">The Application assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting Application types do not reference concrete infrastructure.</returns>
    public static ConditionList ApplicationNeverReferencesConcreteInfrastructure(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.Persistence.EfCore")
            .And()
            .NotHaveDependencyOn("SharedKernel.Persistence.Npgsql")
            .And()
            .NotHaveDependencyOn("SharedKernel.Persistence.Dapper")
            .And()
            .NotHaveDependencyOn("SharedKernel.Messaging.MassTransit")
            .And()
            .NotHaveDependencyOn("SharedKernel.Caching.Redis")
            .And()
            .NotHaveDependencyOn("SharedKernel.Storage.S3")
            .And()
            .NotHaveDependencyOn("SharedKernel.Search.Meilisearch")
            .And()
            .NotHaveDependencyOn("SharedKernel.Search.ElasticSearch");

    // Every namespace of 05.Application (SharedKernel.Application, .Pipeline(.Caching) and .Mediator.MediatR), MediatR and
    // 12.Security. Since WO-086/P-564 the shared contracts live in the Foundation package
    // SharedKernel.Execution, so no SharedKernel.Application namespace is allowed any more.
    private static readonly string[] PersistenceForbiddenNamespaces =
    [
        "SharedKernel.Application",
        "MediatR",
        "SharedKernel.Security",
    ];

    /// <summary>
    /// 06.Persistence reaches the shared <c>IUnitOfWork</c>, <c>IRequestContext</c> and
    /// <c>IAuditTrailWriter</c> through the Foundation package <c>SharedKernel.Execution</c> and must
    /// never reference <c>05.Application</c> (<c>SharedKernel.Application</c>, <c>.Pipeline</c>),
    /// MediatR, or <c>12.Security</c>.
    /// </summary>
    /// <param name="assembly">Any <c>SharedKernel.Persistence.*</c> assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting the assembly's types depend on no forbidden namespace.
    /// </returns>
    /// <remarks>
    /// <para>
    /// P-558 merged the former duplicate contracts (05's local <c>IUnitOfWork</c>/<c>IAuditTrailWriter</c>
    /// seams, 06's <c>IUnitOfWork</c>/<c>ITransactionalUnitOfWork</c>/<c>IAuditTrailWriter</c> and its
    /// <c>ICurrentActorContext</c>/<c>ICurrentTenantContext</c>) into the MediatR-free
    /// <c>SharedKernel.Application.Abstractions</c>, which WO-086/P-564 moved to the Foundation package
    /// <c>SharedKernel.Execution</c>; 06 implements them directly. <c>05.Application</c> carries the mediator adapter
    /// and the pipeline, which persistence must never depend on; <c>12.Security</c> stays out entirely — identity reaches
    /// persistence only through <c>IRequestContext</c>.
    /// </para>
    /// <para>
    /// Namespace-based, so a type-level dependency is caught wherever it appears. Pair it with
    /// <see cref="PersistenceForbiddenAssemblyReferences"/>, which catches a forbidden assembly
    /// reference even when no type from it is used yet.
    /// </para>
    /// </remarks>
    public static ConditionList PersistenceNeverReferencesApplicationOrSecurity(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOnAny(PersistenceForbiddenNamespaces);

    /// <summary>
    /// Returns the names of every assembly <paramref name="assembly"/> references that 06.Persistence
    /// must not: any <c>SharedKernel.Application*</c>, <c>MediatR</c> and any
    /// <c>SharedKernel.Security*</c>. Empty when compliant.
    /// </summary>
    /// <param name="assembly">Any <c>SharedKernel.Persistence.*</c> assembly to evaluate.</param>
    /// <returns>The offending referenced assembly names.</returns>
    public static IReadOnlyList<string> PersistenceForbiddenAssemblyReferences(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name =>
                name is "MediatR"
                || name.StartsWith("SharedKernel.Application", StringComparison.Ordinal)
                || name.StartsWith("SharedKernel.Security", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Hard rule (P-544): <c>SharedKernel.Application.Pipeline</c> must never reference
    /// <c>SharedKernel.Caching</c> (bare prefix — including <c>.Abstractions</c>; that reference
    /// belongs exclusively to the sibling <c>SharedKernel.Application.Pipeline.Caching</c>
    /// package), Polly, <c>Microsoft.Extensions.Hosting</c>, or <c>SharedKernel.Core</c> (the
    /// guard-clause package, merged from the standalone <c>SharedKernel.Guards</c> by
    /// WO-082/P-505).
    /// </summary>
    /// <param name="assembly">The <c>SharedKernel.Application.Pipeline</c> assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting the Pipeline package stays free of these four
    /// dependencies.
    /// </returns>
    public static ConditionList ApplicationBehaviorsNeverReferencesCachingPollyHostingOrCore(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(CachingNamespace)
            .And()
            .NotHaveDependencyOn("Polly")
            .And()
            .NotHaveDependencyOn("Microsoft.Extensions.Hosting")
            .And()
            .NotHaveDependencyOn("SharedKernel.Core");

    /// <summary>
    /// P-544: <c>SharedKernel.Application.Pipeline.Caching</c> — the sole package in
    /// <c>05.Application</c> permitted to reference <c>SharedKernel.Caching.Abstractions</c> — may
    /// reference <c>SharedKernel.Application.Pipeline</c> and <c>SharedKernel.Caching.Abstractions</c>
    /// only; it must never reach a concrete persistence, messaging, or caching-provider package.
    /// </summary>
    /// <param name="assembly">The <c>SharedKernel.Application.Pipeline.Caching</c> assembly to evaluate.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting the Caching package never references concrete
    /// infrastructure.
    /// </returns>
    public static ConditionList ApplicationBehaviorsCachingNeverReferencesConcreteInfrastructure(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(PersistenceNamespace)
            .And()
            .NotHaveDependencyOn(MessagingNamespace)
            .And()
            .NotHaveDependencyOn("SharedKernel.Caching.Redis")
            .And()
            .NotHaveDependencyOn("SharedKernel.Caching.FusionCache");

    /// <summary>
    /// Hard rule: 16.Testing packages must never be referenced by production code.
    /// Testing helpers are dev/test-time only and must never appear as transitive dependencies.
    /// </summary>
    /// <param name="assembly">The production assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting no production type depends on Testing packages.</returns>
    public static ConditionList TestingNeverReferencedByProduction(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(TestingNamespace)
            .And()
            .NotHaveDependencyOn(PersistenceTestingNamespace);

    /// <summary>
    /// The fifteen forbidden capability-domain namespace terms for
    /// <see cref="SearchReferencesOnlyCoreAndContracts"/> — every OTHER numbered domain's package
    /// family. <c>"SharedKernel.Search"</c> is deliberately excluded (self-exclusion by omission —
    /// 09.Search is the domain under test). <c>"SharedKernel.Primitives"</c>,
    /// <c>"SharedKernel.Core"</c>, <c>"SharedKernel.Configuration"</c>,
    /// <c>"SharedKernel.FeatureManagement"</c>, <c>"SharedKernel.Cryptography"</c> (01.Core) and
    /// <c>"SharedKernel.Contracts"</c> (04.Contracts) are permitted and therefore also excluded.
    /// </summary>
    private static readonly string[] SearchForbiddenTerms =
    [
        "SharedKernel.Caching",
        "SharedKernel.Domain",
        "SharedKernel.Application",
        "SharedKernel.Persistence",
        "SharedKernel.Messaging",
        "SharedKernel.Storage",
        "SharedKernel.AI",
        "SharedKernel.Communication",
        "SharedKernel.Security",
        "SharedKernel.ServiceDefaults",
        "SharedKernel.MultiTenancy",
        "SharedKernel.Presentation",
        "SharedKernel.Integration",
        TestingNamespace,
        "SharedKernel.Workflows",
    ];

    /// <summary>
    /// 09.Search — may only reference 01.Core and 04.Contracts. Asserts that the supplied
    /// <c>09.Search</c> assembly has no dependency on any of fifteen forbidden capability-domain
    /// namespace terms — every OTHER numbered domain's package family.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The FIRST method on this class to return <see cref="ConditionList"/><c>[]</c> instead of a
    /// single <see cref="ConditionList"/> — every sibling method on this class predates the newer
    /// domain-boundary-rule-class convention (<c>RedisTopologyRules</c>/
    /// <c>CommunicationLayeringRules</c>/<c>StorageTopologyRules</c>) of returning one
    /// <see cref="ConditionList"/> per forbidden term for per-term failure-message granularity. This
    /// method deliberately follows that newer convention because it is the first
    /// <see cref="SharedKernelLayeringRules"/> method checking against more than two or three
    /// forbidden terms (fifteen — one per every OTHER numbered domain's package family). Caller must
    /// assert <c>.GetResult().IsSuccessful</c> on EACH element.
    /// </para>
    /// <para>
    /// <strong>Rationale:</strong> mirrors <c>09.Search/CLAUDE.md</c>'s own layering wall verbatim:
    /// "09.Search may only reference 01.Core and 04.Contracts. It must never reference 03.Domain,
    /// 05.Application, 06.Persistence, 07.Messaging, 12.Security, or any other capability domain."
    /// </para>
    /// <para>
    /// <strong>Maintenance obligation:</strong> per this file's own Implementation Rules ("If a new
    /// domain (folder XX) is added, the layering rules must be updated in the same PR"), a future
    /// <c>18.NewDomain</c> addition MUST append its package-family term to this list in the SAME PR
    /// that adds the new domain, or this rule will silently under-enforce against it.
    /// </para>
    /// </remarks>
    /// <param name="searchAssembly">
    /// The <c>09.Search</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInSearch).Assembly</c>.
    /// </param>
    /// <returns>
    /// A fifteen-element array of <see cref="ConditionList"/>, one per forbidden term. The caller
    /// must assert <c>.GetResult().IsSuccessful</c> on EACH element.
    /// </returns>
    public static ConditionList[] SearchReferencesOnlyCoreAndContracts(Assembly searchAssembly)
    {
        var results = new ConditionList[SearchForbiddenTerms.Length];

        for (var i = 0; i < SearchForbiddenTerms.Length; i++)
        {
            results[i] = Types
                .InAssembly(searchAssembly)
                .That()
                .HaveNameStartingWith(string.Empty)
                .Should()
                .NotHaveDependencyOn(SearchForbiddenTerms[i]);
        }

        return results;
    }

    /// <summary>
    /// The fifteen forbidden capability-domain namespace terms for
    /// <see cref="IntelligenceReferencesOnlyCoreAndContracts"/> — every OTHER numbered domain's
    /// package family. <c>"SharedKernel.AI"</c> is deliberately excluded (self-exclusion by
    /// omission — 10.Intelligence is the domain under test); <c>"SharedKernel.Search"</c> is
    /// included (09.Search is a sibling domain 10.Intelligence must never reference) — the
    /// symmetric swap versus <see cref="SearchForbiddenTerms"/>. <c>"SharedKernel.Primitives"</c>,
    /// <c>"SharedKernel.Core"</c>, <c>"SharedKernel.Configuration"</c>,
    /// <c>"SharedKernel.FeatureManagement"</c>, <c>"SharedKernel.Cryptography"</c> (01.Core) and
    /// <c>"SharedKernel.Contracts"</c> (04.Contracts) are permitted and therefore also excluded.
    /// </summary>
    private static readonly string[] IntelligenceForbiddenTerms =
    [
        "SharedKernel.Caching",
        "SharedKernel.Domain",
        "SharedKernel.Application",
        "SharedKernel.Persistence",
        "SharedKernel.Messaging",
        "SharedKernel.Storage",
        SearchNamespace,
        "SharedKernel.Communication",
        "SharedKernel.Security",
        "SharedKernel.ServiceDefaults",
        "SharedKernel.MultiTenancy",
        "SharedKernel.Presentation",
        "SharedKernel.Integration",
        TestingNamespace,
        "SharedKernel.Workflows",
    ];

    /// <summary>
    /// 10.Intelligence — may only reference 01.Core and 04.Contracts. Asserts that the supplied
    /// <c>10.Intelligence</c> assembly has no dependency on any of fifteen forbidden capability-domain
    /// namespace terms — every OTHER numbered domain's package family.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added to this class alongside <see cref="SearchReferencesOnlyCoreAndContracts"/>, following
    /// the same precedent that a layering-BOUNDARY check on an existing numbered domain belongs
    /// alongside its siblings, while TOPOLOGY-INTERNAL checks (sibling-package non-reference,
    /// third-party-dependency purity) live in the domain's own dedicated <c>*TopologyRules</c> class
    /// (<see cref="IntelligenceTopologyRules"/>). Follows <see cref="SearchReferencesOnlyCoreAndContracts"/>'s
    /// <see cref="ConditionList"/><c>[]</c>-per-forbidden-term convention exactly, with the SAME
    /// fifteen-term count — the forbidden list swaps <c>"SharedKernel.Search"</c> IN (09.Search is
    /// now a forbidden reference for 10.Intelligence) and <c>"SharedKernel.AI"</c> OUT
    /// (10.Intelligence is now the domain under test, excluded from its own forbidden list by
    /// omission, not by an explicit self-exclusion term). Every other term is unchanged from
    /// <see cref="SearchReferencesOnlyCoreAndContracts"/>'s own list. Caller must assert
    /// <c>.GetResult().IsSuccessful</c> on EACH element.
    /// </para>
    /// <para>
    /// <strong>Rationale:</strong> mirrors <c>10.Intelligence/CLAUDE.md</c>'s own layering wall
    /// verbatim: "10.Intelligence may only reference 01.Core and 04.Contracts. It must never
    /// reference 03.Domain, 05.Application, 06.Persistence, 07.Messaging, 09.Search, 12.Security, or
    /// any other capability domain."
    /// </para>
    /// <para>
    /// <strong>Maintenance obligation:</strong> per this file's own Implementation Rules ("If a new
    /// domain (folder XX) is added, the layering rules must be updated in the same PR"), a future
    /// <c>18.NewDomain</c> addition MUST append its package-family term to BOTH this method's list
    /// AND <see cref="SearchReferencesOnlyCoreAndContracts"/>'s list (and any future sibling of this
    /// shape) in the SAME PR that adds the new domain, or each affected rule will silently
    /// under-enforce against it.
    /// </para>
    /// </remarks>
    /// <param name="intelligenceAssembly">
    /// The <c>10.Intelligence</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInIntelligence).Assembly</c>.
    /// </param>
    /// <returns>
    /// A fifteen-element array of <see cref="ConditionList"/>, one per forbidden term. The caller
    /// must assert <c>.GetResult().IsSuccessful</c> on EACH element.
    /// </returns>
    public static ConditionList[] IntelligenceReferencesOnlyCoreAndContracts(Assembly intelligenceAssembly)
    {
        var results = new ConditionList[IntelligenceForbiddenTerms.Length];

        for (var i = 0; i < IntelligenceForbiddenTerms.Length; i++)
        {
            results[i] = Types
                .InAssembly(intelligenceAssembly)
                .That()
                .HaveNameStartingWith(string.Empty)
                .Should()
                .NotHaveDependencyOn(IntelligenceForbiddenTerms[i]);
        }

        return results;
    }

    /// <summary>
    /// The fourteen forbidden capability-domain namespace terms for
    /// <see cref="WorkflowsReferencesOnlyCoreContractsAndApplication"/> — every OTHER numbered
    /// domain's package family EXCEPT <c>"SharedKernel.Application"</c>, which is deliberately
    /// ABSENT because <c>17.Workflows</c> is permitted THREE upstream domains (01.Core, 04.Contracts,
    /// and 05.Application), not two. <c>"SharedKernel.Workflows"</c> is excluded by omission
    /// (self-exclusion — 17.Workflows is the domain under test). <c>"SharedKernel.Primitives"</c>,
    /// <c>"SharedKernel.Core"</c>, <c>"SharedKernel.Configuration"</c>,
    /// <c>"SharedKernel.FeatureManagement"</c>, <c>"SharedKernel.Cryptography"</c> (01.Core),
    /// <c>"SharedKernel.Contracts"</c> (04.Contracts), and <c>"SharedKernel.Application"</c>
    /// (05.Application) are permitted and therefore also excluded.
    /// </summary>
    private static readonly string[] WorkflowsForbiddenTerms =
    [
        "SharedKernel.Caching",
        "SharedKernel.Domain",
        "SharedKernel.Persistence",
        "SharedKernel.Messaging",
        "SharedKernel.Storage",
        SearchNamespace,
        "SharedKernel.AI",
        "SharedKernel.Communication",
        "SharedKernel.Security",
        "SharedKernel.ServiceDefaults",
        "SharedKernel.MultiTenancy",
        "SharedKernel.Presentation",
        "SharedKernel.Integration",
        TestingNamespace,
    ];

    /// <summary>
    /// 17.Workflows — may only reference 01.Core, 04.Contracts, and 05.Application. Asserts that the
    /// supplied <c>17.Workflows</c> assembly has no dependency on any of fourteen forbidden
    /// capability-domain namespace terms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added to this class alongside <see cref="SearchReferencesOnlyCoreAndContracts"/> and
    /// <see cref="IntelligenceReferencesOnlyCoreAndContracts"/>, following the same precedent that a
    /// layering-BOUNDARY check on an existing numbered domain belongs alongside its siblings, while
    /// TOPOLOGY-INTERNAL checks (the raw-client-accessor-consumption and HealthChecks-dependency
    /// prohibitions specific to <c>17.Workflows</c>) live in the domain's own dedicated
    /// <see cref="WorkflowTopologyRules"/> class. The FIRST layering-boundary method on this class
    /// where the domain under test is permitted THREE upstream domains, not two —
    /// <c>17.Workflows/CLAUDE.md</c>'s own layering wall states "Only 01.Core, 04.Contracts, and
    /// 05.Application are permitted" — so <c>"SharedKernel.Application"</c> is deliberately ABSENT
    /// from the forbidden list (unlike Search's and Intelligence's own fifteen-term lists, which both
    /// forbid it), alongside the usual self-exclusion (<c>"SharedKernel.Workflows"</c>, omitted
    /// because 17.Workflows is the domain under test). Returns <see cref="ConditionList"/><c>[]</c>
    /// (fourteen elements, one per forbidden term), following the same newer
    /// domain-boundary-rule-class convention as its two siblings. Caller must assert
    /// <c>.GetResult().IsSuccessful</c> on EACH element. None of the fourteen terms is a prefix of
    /// <c>"SharedKernel.Workflows"</c> — no self-collision.
    /// </para>
    /// <para>
    /// <strong>Rationale:</strong> mirrors <c>17.Workflows/CLAUDE.md</c>'s own Hard Violations bullet
    /// verbatim: "Referencing 02.Caching, 03.Domain, 06.Persistence, 07.Messaging, 08.Storage,
    /// 09.Search, 10.Intelligence, 11.Communication, 12.Security, 13.ServiceDefaults,
    /// 14.Presentation, or 15.Integration from 17.Workflows. Only 01.Core, 04.Contracts, and
    /// 05.Application are permitted." This method is the exhaustive, all-fourteen-domains mechanical
    /// form of that sentence — the same relationship <see cref="SearchReferencesOnlyCoreAndContracts"/>
    /// and <see cref="IntelligenceReferencesOnlyCoreAndContracts"/> each have to their own domain's
    /// brain.
    /// </para>
    /// <para>
    /// <strong>Maintenance obligation</strong> (carried forward): a future <c>18.NewDomain</c>
    /// addition MUST append its package-family term to THIS method's list AND both of its siblings'
    /// lists (and any future sibling of this <see cref="ConditionList"/><c>[]</c>-per-forbidden-term
    /// shape) in the SAME PR that adds the new domain, or each affected rule will silently
    /// under-enforce against it.
    /// </para>
    /// </remarks>
    /// <param name="workflowsAssembly">
    /// The <c>17.Workflows</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInWorkflows).Assembly</c>.
    /// </param>
    /// <returns>
    /// A fourteen-element array of <see cref="ConditionList"/>, one per forbidden term. The caller
    /// must assert <c>.GetResult().IsSuccessful</c> on EACH element.
    /// </returns>
    public static ConditionList[] WorkflowsReferencesOnlyCoreContractsAndApplication(Assembly workflowsAssembly)
    {
        var results = new ConditionList[WorkflowsForbiddenTerms.Length];

        for (var i = 0; i < WorkflowsForbiddenTerms.Length; i++)
        {
            results[i] = Types
                .InAssembly(workflowsAssembly)
                .That()
                .HaveNameStartingWith(string.Empty)
                .Should()
                .NotHaveDependencyOn(WorkflowsForbiddenTerms[i]);
        }

        return results;
    }
}
