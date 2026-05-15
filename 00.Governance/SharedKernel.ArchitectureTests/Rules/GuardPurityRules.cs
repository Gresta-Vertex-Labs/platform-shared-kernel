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
/// </remarks>
public static class GuardPurityRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every type in
    /// <see cref="IGuardClause"/>'s assembly that implements <see cref="IGuardClause"/>
    /// — except the <c>Guard.Throw</c> companion class — contains no
    /// <see cref="Mono.Cecil.Cil.OpCodes.Throw"/> IL instruction in any method body.
    /// </summary>
    /// <remarks>
    /// The <c>Guard.Throw</c> class (CLR full name <c>SharedKernel.Guards.Guard+Throw</c>)
    /// is excluded by the <see cref="DoesNotContainThrowIlPredicate"/> itself via a full
    /// type-name match.
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
    /// assembly that implements <see cref="IGuardClause"/> contains no
    /// <see cref="Mono.Cecil.Cil.OpCodes.Throw"/> IL instruction in any method body.
    /// </summary>
    /// <param name="guardsAssembly">
    /// The assembly containing <see cref="IGuardClause"/> implementations to inspect.
    /// Typically <c>typeof(IGuardClause).Assembly</c>.
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
