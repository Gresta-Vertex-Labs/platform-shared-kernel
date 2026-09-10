using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;
using SharedKernel.Guards.Clauses;

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
///     <see cref="IGuardClause"/>. These must never throw; they return <c>Error?</c> (null on
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
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// <para>
/// <strong>WO-082/P-508 — hosting assembly changed, namespace did not.</strong>
/// <c>SharedKernel.Guards</c> was merged into <c>SharedKernel.Core</c>; <c>typeof(IGuardClause)</c>
/// now resolves to <c>SharedKernel.Core.dll</c>, an assembly that also hosts base exceptions,
/// BCL extensions, and the railway extensions (<c>ResultTry</c>/<c>ResultCombine</c>) — none of
/// which this rule is meant to police. The <c>.ImplementInterface(typeof(IGuardClause))</c>
/// filter below narrows the NetArchTest selection, but the real re-scoping to the
/// <c>SharedKernel.Guards</c> namespace happens INSIDE <see cref="DoesNotContainThrowIlPredicate"/>
/// itself (see its own remarks) — not here — because NetArchTest's built-in
/// <c>ResideInNamespaceStartingWith</c> selection filter reads each type's raw
/// <c>TypeDefinition.Namespace</c>, which Mono.Cecil always leaves empty for a nested type
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
    /// <see cref="IGuardClause"/>'s hosting assembly, effectively namespaced under
    /// <c>SharedKernel.Guards</c>, that implements <see cref="IGuardClause"/>
    /// — except the <c>Guard.Throw</c> companion class — contains no
    /// <see cref="Mono.Cecil.Cil.OpCodes.Throw"/> IL instruction in any method body.
    /// </summary>
    /// <remarks>
    /// The <c>Guard.Throw</c> class (CLR full name <c>SharedKernel.Guards.Guard+Throw</c>) and
    /// any type outside the <c>SharedKernel.Guards</c> namespace (see class remarks) are both
    /// excluded by <see cref="DoesNotContainThrowIlPredicate"/> itself.
    /// </remarks>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>result.IsSuccessful.Should().BeTrue()</c>.
    /// </returns>
    public static ConditionList GuardAgainstMethodsMustNotThrow()
    {
        var guardsAssembly = typeof(IGuardClause).Assembly;

        return Types
            .InAssembly(guardsAssembly)
            .That()
            .ImplementInterface(typeof(IGuardClause))
            .Should()
            .MeetCustomRule(new DoesNotContainThrowIlPredicate());
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every type in the provided
    /// assembly that implements <see cref="IGuardClause"/> AND is effectively namespaced
    /// under <c>SharedKernel.Guards</c> (see class remarks) contains no
    /// <see cref="Mono.Cecil.Cil.OpCodes.Throw"/> IL instruction in any method body.
    /// </summary>
    /// <param name="guardsAssembly">
    /// The assembly containing <see cref="IGuardClause"/> implementations to inspect.
    /// Typically <c>typeof(IGuardClause).Assembly</c>. Since WO-082/P-508 this is
    /// <c>SharedKernel.Core.dll</c>, which also hosts unrelated types — those are excluded by
    /// namespace, not by which assembly they live in.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion.
    /// </returns>
    public static ConditionList GuardAgainstMethodsMustNotThrow(Assembly guardsAssembly)
    {
        return Types
            .InAssembly(guardsAssembly)
            .That()
            .ImplementInterface(typeof(IGuardClause))
            .Should()
            .MeetCustomRule(new DoesNotContainThrowIlPredicate());
    }
}
