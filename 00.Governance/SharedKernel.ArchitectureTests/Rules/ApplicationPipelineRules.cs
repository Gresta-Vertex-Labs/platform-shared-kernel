using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that enforce the 05.Application extended-pipeline contract
/// the three opt-in behaviors (<c>TracingBehavior</c>,
/// <c>ResilienceBehavior</c>, <c>CacheInvalidationBehavior</c>) reference only abstraction
/// packages, no existing behavior's <c>TRequest</c> constraint accidentally captures the
/// streaming query vocabulary, and no type other than <c>ResilienceBehavior</c> hand-rolls a
/// <c>Task.Delay</c>-based retry loop.
/// </summary>
/// <remarks>
/// <para>
/// All factory methods accept <see cref="Assembly"/> (or <c>params Assembly[]</c>) and return
/// <see cref="ConditionList"/>. Predicates are designed and tested here against contrived
/// in-memory fixture assemblies — <c>00.Governance</c> never references
/// <c>05.Application</c>/<c>05.Application.Behaviors</c> directly (layering: <c>00.Governance</c>
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
    /// <c>"TracingBehavior"</c>, <c>"ResilienceBehavior"</c>, or
    /// <c>"CacheInvalidationBehavior"</c> (exact simple name match, caller-supplied — never
    /// hardcoded inside the predicate) in <paramref name="assemblies"/> has a member, field, or
    /// method-signature reference to a forbidden concrete-infrastructure namespace:
    /// <c>SharedKernel.Caching.FusionCache</c>, <c>SharedKernel.Caching.Redis</c> (bare prefix —
    /// matches Redis.Core and all four Redis capability packages), <c>SharedKernel.Persistence</c>
    /// (excluding <c>SharedKernel.Persistence.Abstractions</c>), <c>SharedKernel.Messaging</c>
    /// (excluding <c>SharedKernel.Messaging.Abstractions</c>).
    /// </summary>
    /// <param name="assemblies">
    /// The assemblies to scan — typically <c>SharedKernel.Application.Behaviors</c>, or a
    /// contrived fixture assembly shaped like it.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> — call <c>.GetResult()</c> to obtain pass/fail information.
    /// When the rule fails, the predicate failure message names the offending behavior type and
    /// the forbidden namespace referenced.
    /// </returns>
    /// <remarks>
    /// Mirrors the existing, already-enforced
    /// <see cref="SharedKernelLayeringRules.ApplicationNeverReferencesConcreteInfrastructure"/>
    /// guarantee, made explicit and behavior-scoped for the three behaviors — the
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
            "ResilienceBehavior",
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
    /// The <c>SharedKernel.Application.Behaviors</c> assembly, or a contrived fixture assembly
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

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type other than exactly
    /// <c>"ResilienceBehavior"</c> (exact simple name match) in
    /// <paramref name="behaviorsAssembly"/> calls
    /// <c>System.Threading.Tasks.Task.Delay</c> (any overload).
    /// </summary>
    /// <param name="behaviorsAssembly">
    /// The <c>SharedKernel.Application.Behaviors</c> assembly, or a contrived fixture assembly
    /// shaped like it.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> — call <c>.GetResult()</c> to obtain pass/fail information.
    /// When the rule fails, the predicate failure message names the offending type, method, and
    /// the <c>Task.Delay</c> call site.
    /// </returns>
    /// <remarks>
    /// This is a fingerprint heuristic, not a full retry-loop detector — <c>Task.Delay</c> is
    /// the one IL-detectable signal common to virtually every hand-rolled retry/backoff loop; a
    /// legitimate non-retry <c>Task.Delay</c> call elsewhere in 05.Application would also be
    /// flagged (none is known to exist at the time of this phase). Extends
    /// <c>05.Application/CLAUDE.md</c>'s existing prohibition on hand-rolled
    /// <c>System.Random</c>/<c>DateTime.UtcNow</c> usage to retry/backoff specifically, now that
    /// <c>ResilienceBehavior</c> exists as the platform-sanctioned alternative
    /// (<c>IRetryableRequest</c> + <c>ApplicationBehaviorsBuilder.AddResilienceBehavior(...)</c>)
    /// — documented as a Hard Violation in <c>05.Application/CLAUDE.md</c> but not previously
    /// mechanically enforced.
    /// </remarks>
    public static ConditionList NoHandRolledRetryLoopOutsideResilienceBehavior(
        Assembly behaviorsAssembly)
        => Types
            .InAssembly(behaviorsAssembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoTaskDelayOutsideResilienceBehaviorPredicate());
}
