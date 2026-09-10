using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicate that bans bare string literals at health-check registration
/// call sites when a sibling string constants class in the same assembly already exposes that
/// exact value.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Motivating incident.</strong> An audit of the platform's own host composition found two
/// generations of the same mistake: one well-known-string constants class was built correctly,
/// but several sibling files kept hardcoding default health-check names as bare string literals
/// instead of extending the same discipline. Nothing mechanically caught the inconsistency. This
/// rule closes that gap.
/// </para>
/// <para>
/// <strong>Additive, not overlapping, with <see cref="HealthCheckTagIntegrityRules"/>.</strong>
/// <see cref="HealthCheckTagIntegrityRules"/> enforces tag mutual-exclusivity semantics
/// (<c>"live"</c> vs <c>"ready"</c>) — a domain-specific concern hardcoded to those two tag
/// values. This rule enforces a source-discipline concern (bare literals vs. named constants)
/// that is fully generic — it never references any concrete constants-class name by string
/// literal anywhere in its implementation. The two rule groups must never be merged or confused:
/// they answer different questions and one is intentionally narrow while the other is
/// intentionally general.
/// </para>
/// <para>
/// <strong>Detection-surface decision.</strong> A bare literal is only flagged when its value
/// exactly matches a value already exposed by a detected string constants class
/// (<see cref="StringConstantsClassDetector"/>) in the same assembly. A pure "any literal + any
/// constants class coexist" heuristic was rejected as too noisy — it would fire on every
/// health-check call site in an assembly that happens to contain any string-constants class
/// anywhere, regardless of relevance. When no string constants class exists yet in the assembly,
/// the rule passes vacuously — matching the "before" state where the platform's first
/// well-known-string constants class did not yet exist and there was nothing yet to enforce.
/// </para>
/// </remarks>
public static class HealthCheckConstantsUsageRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no health-check registration call
    /// site (<c>IHealthChecksBuilder.Add</c>, <c>AddCheck</c>, or the
    /// <c>HealthCheckRegistration</c> constructor) in <paramref name="assembly"/> passes a bare
    /// string literal whose value exactly matches a value already exposed by a string constants
    /// class detected in the same assembly.
    /// </summary>
    /// <param name="assembly">
    /// The assembly (or a fixture shaped like it) to scan for both the constants-class shape and
    /// the health-check call sites.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> — call <c>.GetResult()</c> to obtain pass/fail information.
    /// When the rule fails, <c>FailingTypeNames</c> names the offending type.
    /// </returns>
    public static ConditionList NoBareHealthCheckLiteralWhereConstantsExist(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoBareHealthCheckLiteralWhereConstantsExistPredicate());
}
