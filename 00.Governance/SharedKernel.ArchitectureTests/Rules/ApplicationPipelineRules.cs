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
/// <see cref="ConditionList"/>. Each is tested here against contrived in-memory fixture assemblies and
/// against the real <c>SharedKernel.Application.Pipeline</c>/<c>.Pipeline.Caching</c> assemblies.
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
    /// The assemblies to scan — typically <c>SharedKernel.Application.Pipeline</c> (for
    /// <c>TracingBehavior</c>) and <c>SharedKernel.Application.Pipeline.Caching</c> (for
    /// <c>CacheInvalidationBehavior</c>, which lives in that sibling package as of P-544), or a
    /// contrived fixture assembly shaped like either.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> — call <c>.GetResult()</c> to obtain pass/fail information.
    /// When the rule fails, the predicate failure message names the offending behavior type and
    /// the forbidden namespace referenced.
    /// </returns>
    /// <remarks>
    /// The pipeline packages are Host tier, which the tier matrix allows to reference adapters; this rule
    /// keeps the two named behaviors provider-neutral anyway, made explicit and behavior-scoped — the
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
    /// <c>TRequest</c> generic-parameter constraint that structurally satisfies the kernel's
    /// <c>IStreamQuery&lt;TResponse&gt;</c> (directly or via interface closure).
    /// </summary>
    /// <param name="behaviorsAssembly">
    /// The <c>SharedKernel.Application.Pipeline</c> assembly, or a contrived fixture assembly
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
    /// capture <c>IStreamQuery&lt;TResponse&gt;</c>.
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
    /// <c>SharedKernel.Application.Pipeline</c> must never reference <c>SharedKernel.Caching</c> (bare prefix, including
    /// <c>.Abstractions</c>: that reference belongs to the sibling <c>SharedKernel.Application.Pipeline.Caching</c>
    /// package, P-544), Polly or <c>Microsoft.Extensions.Hosting</c>. It may reference <c>SharedKernel.Core</c>, which it uses to
    /// turn a failed result into its exception (<c>error.ToException()</c>) — P-579 allowed that reference.
    /// </summary>
    /// <remarks>
    /// The pipeline is Host tier, so the tier matrix would allow every one of these references. This rule keeps the
    /// core pipeline free of optional dependencies, so a service that does not cache pays for no cache package.
    /// </remarks>
    /// <param name="assembly">The <c>SharedKernel.Application.Pipeline</c> assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting the pipeline package stays free of these three dependencies.</returns>
    public static ConditionList PipelineNeverReferencesCachingPollyOrHosting(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.Caching")
            .And()
            .NotHaveDependencyOn("Polly")
            .And()
            .NotHaveDependencyOn("Microsoft.Extensions.Hosting");

    /// <summary>
    /// <c>SharedKernel.Application.Pipeline.Caching</c> may reach <c>SharedKernel.Caching.Abstractions</c> but never a
    /// concrete persistence, messaging or caching-provider package (P-544).
    /// </summary>
    /// <remarks>
    /// The caching pipeline is Host tier, so the tier matrix would allow a provider reference; this rule keeps the
    /// caching behaviors provider-neutral.
    /// </remarks>
    /// <param name="assembly">The <c>SharedKernel.Application.Pipeline.Caching</c> assembly to evaluate.</param>
    /// <returns>A <see cref="ConditionList"/> asserting the caching pipeline never references concrete infrastructure.</returns>
    public static ConditionList PipelineCachingNeverReferencesConcreteInfrastructure(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn("SharedKernel.Persistence")
            .And()
            .NotHaveDependencyOn("SharedKernel.Messaging")
            .And()
            .NotHaveDependencyOn("SharedKernel.Caching.Redis")
            .And()
            .NotHaveDependencyOn("SharedKernel.Caching.FusionCache");
}
