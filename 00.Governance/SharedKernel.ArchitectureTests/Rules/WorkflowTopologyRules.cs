using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that mechanically enforce the <c>17.Workflows</c> package
/// topology (<c>SharedKernel.Workflows.Temporal</c>) documented in prose by
/// <c>17.Workflows/CLAUDE.md</c>. Introduced in WO-046 P-290.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The first <c>*TopologyRules</c> class in this domain scoped to a domain with NO sibling
/// provider packages.</strong> Unlike <see cref="RedisTopologyRules"/>/<see cref="StorageTopologyRules"/>/
/// <see cref="SearchTopologyRules"/>/<see cref="IntelligenceTopologyRules"/>, <c>17.Workflows</c> has a
/// single package — <c>SharedKernel.Workflows.Temporal</c> is both the abstraction surface and the
/// Temporal-specific implementation — so this class carries NO
/// <c>AbstractionsHasNoThirdPartyDependencies</c>/<c>ProviderPackagesNeverReferenceEachOther</c> pair.
/// Do not add either shape to this class unless <c>SharedKernel.Workflows.Temporal</c> is ever split
/// into an <c>.Abstractions</c> + <c>.Temporal</c> pair — until then, third-party-dependency purity
/// for this package is <see cref="SharedKernelLayeringRules.WorkflowsReferencesOnlyCoreContractsAndApplication"/>'s
/// job (capability-domain terms) plus <see cref="NoHealthChecksDependencyInWorkflows"/> (the one
/// specific third-party-package prohibition <c>17.Workflows/CLAUDE.md</c> names explicitly).
/// </para>
/// <para>
/// <c>17.Workflows/CLAUDE.md</c>'s own cross-domain ask describes this class as "modelled one-for-one
/// on <c>StorageTopologyRules</c>/<c>SearchTopologyRules</c>"; for a single-package domain that means
/// restating the SAME two documented <c>NotHaveDependencyOn</c> gotchas (namespace <c>StartsWith</c>,
/// no trailing dot; never check a package against its own identifying term) while the actual
/// predicates are narrower, single-package equivalents of those classes' topology-INTERNAL concerns.
/// </para>
/// <para>
/// No new SK diagnostic ID, zero new NuGet dependency — <see cref="NoHealthChecksDependencyInWorkflows"/>
/// is a pure NetArchTest namespace-dependency check; <see cref="NoRawClientAccessorConsumptionInRepo"/>
/// reuses the established Mono.Cecil <c>TypeDefinition.Methods</c> (constructor-parameter) and
/// <c>TypeDefinition.Fields</c> inspection pattern already used throughout this project (e.g.
/// <c>NoEncryptionRotationJobInjectionPredicate</c>, <c>NoDbContextTransactionInApplicationPredicate</c>)
/// — the existing Mono.Cecil &gt;= 0.11.5 reference already covers it.
/// </para>
/// </remarks>
public static class WorkflowTopologyRules
{
    /// <summary>
    /// The single forbidden dependency term for <see cref="NoHealthChecksDependencyInWorkflows"/>.
    /// </summary>
    private const string HealthChecksNamespace = "Microsoft.Extensions.Diagnostics.HealthChecks";

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in any of the supplied
    /// <paramref name="repoAssemblies"/> consumes <c>ITemporalRawClientAccessor</c> as a constructor
    /// parameter or a field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses <see cref="NoRawClientAccessorConsumptionPredicate"/>. Carries NO internal exemption —
    /// mirrors <c>GrpcNeverReferencesContracts</c>'s "no exemption permitted" precedent. No exemption
    /// is needed for the accessor's own DI-registration wiring code either: that code PRODUCES an
    /// <c>ITemporalRawClientAccessor</c> instance (via a factory delegate passed to a DI registration
    /// call) rather than CONSUMING one as a constructor/field dependency, so it is never a false
    /// positive under this constructor/field-only detection technique.
    /// </para>
    /// <para>
    /// <strong>Rationale:</strong> mechanizes gate 3 of <c>17.Workflows/CLAUDE.md</c>'s own three-gate
    /// <c>ITemporalRawClientAccessor</c> escape-hatch discipline verbatim: "A 00.Governance
    /// architecture test asserts no type inside this repo consumes it." The accessor is the genuine
    /// last resort for Visibility API queries, schedules, namespace administration, and Nexus
    /// operations that this package deliberately does not model — but it bypasses tenant scoping and
    /// workflow-id composition entirely (stated IN CAPITALS on the accessor's own XML doc per that
    /// same brain section), so it must never be a dependency of any type living inside this
    /// platform's own mono-repo; only a CONSUMING microservice, having read and accepted that
    /// warning, may ever construct-inject it, and even then only after its own composition root calls
    /// <c>.AllowRawClientAccess()</c> — a check this rule does not attempt to correlate.
    /// </para>
    /// <para>
    /// <strong>Scope note:</strong> "no type inside this repo" is read literally, per
    /// <c>17.Workflows/CLAUDE.md</c>'s own wording — this rule asserts an absolute prohibition on the
    /// SharedKernel mono-repo's own packages, not a per-consuming-microservice correlation with
    /// whether <c>.AllowRawClientAccess()</c> was called. A consuming microservice's own use of the
    /// accessor (after opting in) is verified by that microservice's own test suite, not by this
    /// platform-level rule — the same jurisdiction boundary already established for every other
    /// "repo-internal purity" rule in this file (e.g. <c>DomainLayerPurityRules</c>,
    /// <c>ContractsPurityRules</c>).
    /// </para>
    /// </remarks>
    /// <param name="repoAssemblies">
    /// The in-repo assemblies to check — typically <c>SharedKernel.Workflows.Temporal</c> itself, and
    /// any other in-repo <c>SharedKernel.*</c> assembly the caller chooses to include.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>result.IsSuccessful.Should().BeTrue()</c> or <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList NoRawClientAccessorConsumptionInRepo(params Assembly[] repoAssemblies) =>
        Types
            .InAssemblies(repoAssemblies)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoRawClientAccessorConsumptionPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that <c>SharedKernel.Workflows.Temporal</c> has
    /// no dependency on <c>"Microsoft.Extensions.Diagnostics.HealthChecks"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Single <c>Types.InAssembly(workflowsAssembly).Should().NotHaveDependencyOn(...)</c> call —
    /// deliberately the NARROW full term, not a bare <c>"Microsoft.Extensions"</c> prefix, mirroring
    /// <see cref="IntelligenceTopologyRules.NoHealthChecksDependencyAcrossIntelligencePackages"/>'s
    /// precedent exactly: <c>17.Workflows</c> legitimately needs OTHER <c>Microsoft.Extensions.*</c>
    /// packages (Hosting, DependencyInjection, Logging, Options) for its own DI/hosting wiring — only
    /// the HealthChecks-specific term is forbidden.
    /// </para>
    /// <para>
    /// <strong>Rationale:</strong> mechanizes <c>17.Workflows/CLAUDE.md</c>'s own Hard Violations
    /// bullet verbatim: "Implementing <c>IHealthCheck</c>, or referencing
    /// <c>Microsoft.Extensions.Diagnostics.HealthChecks</c>, anywhere in <c>17.Workflows</c>.
    /// <c>ProbeAsync</c> returning <c>Result&lt;WorkflowServiceHealth&gt;</c> is the primitive; the
    /// adapter is <c>13.ServiceDefaults</c>'s responsibility" — mirroring the
    /// <c>06.Persistence</c>/<c>08.Storage</c>/<c>09.Search</c>/<c>10.Intelligence</c>
    /// readiness-probe split precedent.
    /// </para>
    /// </remarks>
    /// <param name="workflowsAssembly">
    /// The <c>SharedKernel.Workflows.Temporal</c> assembly under test — supply via
    /// <c>typeof(SomeTypeInWorkflows).Assembly</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>result.IsSuccessful.Should().BeTrue()</c> or <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList NoHealthChecksDependencyInWorkflows(Assembly workflowsAssembly) =>
        Types
            .InAssembly(workflowsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(HealthChecksNamespace);
}
