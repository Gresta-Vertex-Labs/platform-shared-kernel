using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Helpers;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that enforce the guard clause functional-path purity contract.
/// </summary>
/// <remarks>
/// <para>
/// The SharedKernel guard system follows a two-path design:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <b>Functional path</b> — <c>Guard.Against.*</c> extension methods on types implementing
///     <c>IGuardClause</c>. These must never throw; they return <c>Error?</c> (null on
///     pass, non-null on violation).
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Imperative path</b> — <c>Guard.Throw.*</c> methods in the <c>Guard.Throw</c> nested
///     class. These legitimately throw <c>DomainException</c> and are excluded from the purity
///     check.
///     </description>
///   </item>
/// </list>
/// <para>
/// <strong>Namespace scope, not assembly scope.</strong> <c>SharedKernel.Guards</c> was merged
/// into <c>SharedKernel.Core</c> with the C# namespace deliberately preserved, so
/// <c>IGuardClause</c> now lives in an assembly that also hosts base exceptions, BCL extensions,
/// and the railway extensions (<c>ResultTry</c>/<c>ResultCombine</c>) — none of which this rule
/// is meant to police. The <c>.ImplementInterface(...)</c> filter below narrows the NetArchTest
/// selection, but the real re-scoping to the <c>SharedKernel.Guards</c> namespace happens INSIDE
/// <see cref="DoesNotContainThrowIlPredicate"/> itself (see its own remarks) — not here — because
/// NetArchTest's built-in <c>ResideInNamespaceStartingWith</c> selection filter reads each type's
/// raw <c>TypeDefinition.Namespace</c>, which Mono.Cecil always leaves empty for a nested type
/// (e.g. <c>Guard/DefaultGuardClause</c>, the sole real <c>IGuardClause</c> implementor).
/// Applying that filter here would have silently excluded every guard type and made the whole
/// rule vacuously pass. The predicate instead walks up to each type's outermost enclosing type
/// to resolve its effective namespace before deciding whether it is in scope.
/// </para>
/// </remarks>
public static class GuardPurityRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every type in
    /// <paramref name="guardsAssembly"/> that implements <paramref name="guardClauseInterface"/>
    /// AND is effectively namespaced under <c>SharedKernel.Guards</c> (see class remarks)
    /// contains no <see cref="Mono.Cecil.Cil.OpCodes.Throw"/> IL instruction in any method body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>Guard.Throw</c> companion class (CLR full name
    /// <c>SharedKernel.Guards.Guard+Throw</c>) and any type outside the
    /// <c>SharedKernel.Guards</c> namespace are both excluded by
    /// <see cref="DoesNotContainThrowIlPredicate"/> itself.
    /// </para>
    /// <para>
    /// <strong>Usage.</strong> Both anchors come from the caller so this package needs no
    /// reference to <c>SharedKernel.Core</c>:
    /// </para>
    /// <code>
    /// var rule = GuardPurityRules.GuardAgainstMethodsMustNotThrow(
    ///     typeof(IGuardClause).Assembly,
    ///     typeof(IGuardClause));
    /// AssertRule(rule);
    /// </code>
    /// </remarks>
    /// <param name="guardsAssembly">
    /// The assembly containing the guard-clause implementations to inspect. Typically
    /// <c>typeof(IGuardClause).Assembly</c>. That assembly also hosts types this rule is not
    /// meant to police; they are excluded by namespace, not by which assembly they live in.
    /// </param>
    /// <param name="guardClauseInterface">
    /// The guard-clause marker interface, i.e. <c>typeof(SharedKernel.Guards.Clauses.IGuardClause)</c>.
    /// Supplied by the caller rather than hard-bound here so this package declares no dependency
    /// on <c>SharedKernel.Core</c>.
    /// </param>
    /// <returns>A <see cref="ConditionList"/> ready for assertion.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="guardClauseInterface"/> is not an interface — which would select zero
    /// types and make the rule pass vacuously.
    /// </exception>
    public static ConditionList GuardAgainstMethodsMustNotThrow(
        Assembly guardsAssembly,
        Type guardClauseInterface)
    {
        RuleAnchor.NotNull(guardsAssembly, nameof(guardsAssembly));
        RuleAnchor.Interface(guardClauseInterface, nameof(guardClauseInterface));

        return Types
            .InAssembly(guardsAssembly)
            .That()
            .ImplementInterface(guardClauseInterface)
            .Should()
            .MeetCustomRule(new DoesNotContainThrowIlPredicate());
    }
}
