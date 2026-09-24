using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that enforce the 05.Application extended-pipeline contract:
/// the named behaviors (<c>TracingBehavior</c>, <c>CacheInvalidationBehavior</c>) reference only
/// abstraction packages, and no existing behavior's <c>TRequest</c> constraint accidentally
/// captures the streaming query vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// All factory methods accept <see cref="Assembly"/> (or <c>params Assembly[]</c>) and return
/// <see cref="ConditionList"/>. Predicates are designed and tested here against contrived
/// in-memory fixture assemblies — <c>00.Governance</c> never references
/// <c>05.Application</c> directly (layering: <c>00.Governance</c>
/// references nothing). The owning domain (<c>05.Application</c>) is responsible for invoking
/// the existing cross-domain consumption pattern already established for
/// <see cref="CachingAbstractionRules"/>/<see cref="RedisTopologyRules"/> (consumed by
/// 02.Caching's own test suites) and <see cref="PersistenceLayerProtectionRules"/> (consumed by
/// 06.Persistence's own test suites).
/// </para>
/// </remarks>
public static class ApplicationPipelineRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type named
    /// <c>"TracingBehavior"</c> or <c>"CacheInvalidationBehavior"</c> (exact simple name match,
    /// caller-supplied — never hardcoded inside the predicate) in <paramref name="assemblies"/>
    /// has a member, field, or method-signature reference to a forbidden concrete-infrastructure
    /// namespace: <c>SharedKernel.Caching.FusionCache</c>, <c>SharedKernel.Caching.Redis</c>
    /// (bare prefix — matches Redis.Core and all four Redis capability packages),
    /// <c>SharedKernel.Persistence</c> (excluding <c>SharedKernel.Persistence.Abstractions</c>),
    /// <c>SharedKernel.Messaging</c> (excluding <c>SharedKernel.Messaging.Abstractions</c>).
    /// </summary>
    /// <param name="assemblies">
    /// The assemblies to scan — typically <c>SharedKernel.Application</c> (for
    /// <c>TracingBehavior</c>) and <c>SharedKernel.Application.Caching</c> (for
    /// <c>CacheInvalidationBehavior</c>, which lives in that sibling package as of P-544), or a
    /// contrived fixture assembly shaped like either.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> — call <c>.GetResult()</c> to obtain pass/fail information.
    /// When the rule fails, the predicate failure message names the offending behavior type and
    /// the forbidden namespace referenced.
    /// </returns>
    /// <remarks>
    /// Mirrors the existing, already-enforced
    /// <see cref="SharedKernelLayeringRules.ApplicationNeverReferencesConcreteInfrastructure"/>
    /// guarantee, made explicit and behavior-scoped for the two behaviors — the
    /// same purity expectation <c>CachingBehavior</c> (<c>SharedKernel.Caching.Abstractions</c>
    /// only) already satisfies by construction. Abstractions-only references remain permitted;
    /// only concrete provider packages are forbidden.
    /// </remarks>
    public static ConditionList BehaviorsNeverReferenceConcreteInfrastructure(
        params Assembly[] assemblies)
    {
        var behaviorTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "TracingBehavior",
            "CacheInvalidationBehavior",
        };

        var forbiddenNamespacePrefixes = new HashSet<string>(StringComparer.Ordinal)
        {
            "SharedKernel.Caching.FusionCache",
            "SharedKernel.Caching.Redis",
            "SharedKernel.Persistence",
            "SharedKernel.Messaging",
        };

        return Types
            .InAssemblies(assemblies)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(
                new NoConcreteInfrastructureReferenceOnNamedBehaviorsPredicate(
                    behaviorTypeNames,
                    forbiddenNamespacePrefixes));
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type implementing the open
    /// generic <c>IPipelineBehavior&lt;,&gt;</c> in <paramref name="behaviorsAssembly"/> has a
    /// <c>TRequest</c> generic-parameter constraint that structurally satisfies MediatR's
    /// <c>IStreamRequest&lt;TResponse&gt;</c> (directly or via interface closure).
    /// </summary>
    /// <param name="behaviorsAssembly">
    /// The <c>SharedKernel.Application</c> assembly, or a contrived fixture assembly
    /// shaped like it.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> — call <c>.GetResult()</c> to obtain pass/fail information.
    /// When the rule fails, the predicate failure message names the offending behavior type and
    /// the matching constraint type.
    /// </returns>
    /// <remarks>
    /// This is a structural IL generic-constraint check, not a runtime DI resolution test — it
    /// fails at the architecture-test stage, earlier than any runtime wiring attempt, if a
    /// future behavior's <c>TRequest</c> constraint is loosened in a way that could accidentally
    /// capture <c>IStreamQuery&lt;TResponse&gt;</c>/<c>IStreamRequest&lt;TResponse&gt;</c>.
    /// <c>05.Application/CLAUDE.md</c> documents as an explicit, deliberate design decision that
    /// none of the platform's pipeline behaviors apply to the streaming query vocabulary
    /// — this rule makes that documented fact mechanically verified rather than merely
    /// asserted in prose.
    /// </remarks>
    public static ConditionList NoExistingBehaviorMatchesStreamRequestConstraint(
        Assembly behaviorsAssembly)
        => Types
            .InAssembly(behaviorsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoGenericConstraintMatchesStreamRequestPredicate());
}
